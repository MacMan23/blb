﻿using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using System.Runtime.InteropServices;
using B83.Win32;
using System.Threading;

public class FileSystem : MonoBehaviour
{
  readonly static public string s_FilenameExtension = ".blb";
  readonly static public string s_RootDirectoryName = "Basic Level Builder";
  readonly static public string s_DateTimeFormat = "h-mm-ss.ff tt, ddd d MMM yyyy";
  readonly static public int s_MaxAutoSaveCount = 20;
  readonly static public int s_MaxManualSaveCount = 100;
  readonly static bool s_ShouldCompress = false;
  static string s_EditorVersion;

  public string m_DefaultDirectoryName = "Default Project";
  public UiSaveFileItem m_HistoryItemPrefab;
  public UiListView m_SaveList;
  public TileGrid m_TileGrid;

  UnityDragAndDropHook m_DragAndDropHook;

  ModalDialogMaster m_ModalDialogMaster;
  [SerializeField]
  ModalDialogAdder m_OverrideDialogAdder;
  [SerializeField]
  ModalDialogAdder m_SaveAsDialogAdder;
  string m_CurrentDirectoryPath;
  string m_PendingSaveFullPath = "";

  bool m_IsTempFile = false;
  string m_MountedSaveFilePath = "";
  FileData m_MountedFileData;
  Header m_MountedFileHeader;

  // A thread to run when saving should be performed.
  // Only one save thread is run at once.
  private Thread m_SavingThread;
  // A queue of events that the saving thread will enqueue for the main thread
  private readonly MainThreadDispatcher m_MainThreadDispatcher = new();

  [DllImport("__Internal")]
  private static extern void SyncFiles();

  #region FileStructure classes
  [Serializable]
  class FileData
  {
    public FileData()
    {
      m_ManualSaves = new List<LevelData>();
      m_AutoSaves = new List<LevelData>();
    }
    public List<LevelData> m_ManualSaves;
    public List<LevelData> m_AutoSaves;
  }

  [Serializable]
  class Header
  {
    public Header(string ver = "", bool shouldCompress = false)
    {
      m_BlbVersion = ver;
      m_IsDataCompressed = shouldCompress;
    }
    public string m_BlbVersion;
    public bool m_IsDataCompressed = false;
  }

  [Serializable]
  public class LevelData
  {
    public LevelData()
    {
      m_AddedTiles = new List<TileGrid.Element>();
      m_RemovedTiles = new List<Vector2Int>();
    }

    public int m_Version;
    // The manual save version the auto save branched off from
    public int m_BranchVersion;
    public DateTime m_TimeStamp;
    public List<TileGrid.Element> m_AddedTiles;
    public List<Vector2Int> m_RemovedTiles;
  }
  #endregion

  // Start is called before the first frame update
  void Start()
  {
    s_EditorVersion = Application.version;
    m_ModalDialogMaster = FindObjectOfType<ModalDialogMaster>();

    SetDirectoryName(m_DefaultDirectoryName);
  }

  void Update()
  {
    // Calls the unity functions that the save thread can not 
    m_MainThreadDispatcher.Update();
  }

  private void OnEnable()
  {
    m_DragAndDropHook = new UnityDragAndDropHook();
    m_DragAndDropHook.InstallHook();
    m_DragAndDropHook.OnDroppedFiles += OnDroppedFiles;
  }


  private void OnDisable()
  {
    m_DragAndDropHook.UninstallHook();
    m_DragAndDropHook.OnDroppedFiles -= OnDroppedFiles;
  }

  // Check if any file got deleted when we were off the game
  private void OnApplicationFocus(bool focus)
  {
    if (focus)
    {
      if (IsFileMounted() && !File.Exists(m_MountedSaveFilePath))
      {
        var errorString = $"Error: File with path \"{m_MountedSaveFilePath}\" could not be found." + Environment.NewLine +
               "Loaded level has been saved with the same name.";
        StatusBar.Print(errorString);
        Debug.LogWarning(errorString);
        var tempPath = Path.GetFileNameWithoutExtension(m_MountedSaveFilePath);
        UnmountFile();
        SaveAs(tempPath);
      }

      m_SaveList.ValidateAllItems();
    }
  }

  void CreateFileData()
  {
    m_MountedFileHeader = new(s_EditorVersion, s_ShouldCompress);
    m_MountedFileData = new();
  }

  void ClearFileData()
  {
    m_MountedFileHeader = null;
    m_MountedFileData = null;
  }

  bool FileDataExists()
  {
    return m_MountedFileData != null;
  }

  void UnmountFile()
  {
    m_MountedSaveFilePath = "";
  }

  void MountFile(string filepath)
  {
    m_MountedSaveFilePath = filepath;
  }

  bool IsFileMounted()
  {
    return !String.IsNullOrEmpty(m_MountedSaveFilePath);
  }

  void OnDroppedFiles(List<string> paths, POINT dropPoint)
  {
    if (m_ModalDialogMaster.m_Active || GlobalData.AreEffectsUnderway() || GlobalData.IsInPlayMode())
      return;

    var validPaths = paths.Where(path => path.EndsWith(".blb")).ToList();

    if (validPaths.Count == 0)
      StatusBar.Print("Drag and drop only supports <b>.blb</b> files.");
    else
      LoadFromFullPath(validPaths[0]);
  }

  public void ManualSave()
  {
    Save(false);
  }

  public void Autosave()
  {
    Save(true);
  }

  public void SaveAs(string name)
  {
    Save(false, name);
  }

  void Save(bool autosave, string name = null)
  {
    if (GlobalData.AreEffectsUnderway())
      return;

    // If we have a thread running
    if (m_SavingThread != null && m_SavingThread.IsAlive)
      return;

    string fullPath;
    // If we are doing a SAVE AS
    if (name != null)
    {
      fullPath = Path.Combine(m_CurrentDirectoryPath, name + s_FilenameExtension);

      // Give prompt if we are going to write to and existing file
      if (File.Exists(fullPath))
      {
        m_PendingSaveFullPath = fullPath;

        m_OverrideDialogAdder.RequestDialogsAtCenterWithStrings(Path.GetFileName(fullPath));
        return;
      }
    }
    else
    {
      if (IsFileMounted())
      {
        // If we are doing a save, but we only have a temp file
        if (m_IsTempFile && !autosave)
        {
          // request a name for a new file to save to
          m_SaveAsDialogAdder.RequestDialogsAtCenterWithStrings();
          return;
        }
        else
        {
          fullPath = m_MountedSaveFilePath;

          // If our mounted file is deleted/missing
          if (!File.Exists(m_MountedSaveFilePath))
          {
            // Because of the file validation on application focus, this SHOULD never happen.
            // But to be save incase the file is deleted while playing the game, do this
            // TODO, get this error to overwrite or concat with the saved message
            var errorString = $"Error: File with path \"{m_MountedSaveFilePath}\" could not be found." + Environment.NewLine +
              "A new file has been made for this save.";
            StatusBar.Print(errorString);
            Debug.LogWarning(errorString);
            RemoveHistoryItem(m_SaveList, m_MountedSaveFilePath);
            UnmountFile();
          }
        }
      }
      else
      {
        // We are doing a manual or auto save with no mounted file
        // If an auto save, create a temp file and write to that
        if (autosave)
        {
          fullPath = CreateTempFileName();
          m_IsTempFile = true;
          // Mount temp file so we can check when the full file path isn't the temp file
          MountFile(fullPath);
        }
        else
        {
          // We are doing a manual save,
          // request a name for the new file to save to
          m_SaveAsDialogAdder.RequestDialogsAtCenterWithStrings();
          return;
        }
      }
    }

    StartSavingThread(fullPath, autosave, name != null);
  }

  public void ConfirmOverwrite()
  {
    StartSavingThread(m_PendingSaveFullPath, false);
  }

  void StartSavingThread(string fullPath, bool autosave, bool isSaveAs = false)
  {
    // Copy the map data into a buffer to use for the saving thread.
    m_TileGrid.CopyGridBuffer();

    // Define parameters for the branched thread function
    object[] parameters = { fullPath, autosave };

    // Create a new thread and pass the ParameterizedThreadStart delegate
    if (isSaveAs)
      m_SavingThread = new Thread(new ParameterizedThreadStart(SavingThreadFlatten));
    else
      m_SavingThread = new Thread(new ParameterizedThreadStart(SavingThread));

    m_SavingThread.Start(parameters);
  }

  void SavingThreadFlatten(object threadParameters)
  {
    var startTime = DateTime.Now;

    // Extract the parameters from the object array
    object[] parameters = (object[])threadParameters;

    // Access the parameters
    string fullPath = (string)parameters[0];
    bool overwriting = File.Exists(fullPath);

    CreateFileData();

    m_TileGrid.GetLevelData(out LevelData levelData);

    levelData.m_TimeStamp = DateTime.Now;
    levelData.m_Version = 1;

    m_MountedFileData.m_ManualSaves.Add(levelData);

    WriteMountedDataToFile(fullPath, overwriting, startTime, false, false);
  }

  void SavingThread(object threadParameters)
  {
    var startTime = DateTime.Now;

    // Extract the parameters from the object array
    object[] parameters = (object[])threadParameters;

    // Access the parameters
    string fullPath = (string)parameters[0];
    bool autosave = (bool)parameters[1];
    bool overwriting = File.Exists(fullPath);
    // TODO, Don't auto save if the diffences from the last auto save are the same. Ie no unsaved changes.
    #region Add level changes to level data
    // Edge cases
    // #: Overwriting, MountedFile, Diffrences, Saving to mounted file
    // 1: 1, 0, 0, 0 (Save as; we are writing to an existing file, yet we have no mounted file. Thus we just save our editor level) [TileGrid]
    // 2: 1, 1, 0, 0 (Overwrite save to our mounted file or another file. No changes, so just copy our file over) [File copy]
    // 3: 1, 1, 1, 0 (Overwrite save to our mounted file or another file. Add changes to mounted file string) [oldSave + diff]
    // 4: 0, 1, 0, 0 (Save as; Copy our mounted file to a new file) [File copy]
    // 5: 0, 1, 1, 0 (Save as; Copy our level with the diffrences added to a new file) [oldSave + diff]
    // 6: 0, 0, 0, 0 (Save as; Write editor level to file) [TileGrid]
    // 7: 1, 1, 0, 1 (Skip, We are saving to our own file, yet we have no diffrences) [return]
    // 8: 1, 1, 1, 1 (Save to our file with the diffrences) [oldSave + diff]
    // We can't have diffrences if we don't have a mounted file
    // We can only save to the mounted file if the file exist, meaning overwriting is true.
    // We can't save to the mounted file if we have no mounted file

    // If we will be copying the mounted file over to a diffrent file
    bool copyFile = false;
    LevelData levelData;
    bool hasDifferences;
    if (autosave)
      hasDifferences = m_TileGrid.GetDifferencesForAutoSave(out levelData);
    else
      hasDifferences = m_TileGrid.GetDifferences(out levelData);

    // If we are writting to our own file yet we have no changes, skip the save
    // Or we are writting to a temp file with no changes, ignore write
    if (overwriting && IsFileMounted() && fullPath.Equals(m_MountedSaveFilePath) && !hasDifferences)
    {
      // #7
      var errorString = "Skipped save because there is nothing new to save";
      m_MainThreadDispatcher.Enqueue(() => StatusBar.Print(errorString));
      Debug.Log(errorString);
      return;
    }

    // TODO, see where we need to set and reset the m_MountedfileData
    // If we don't have a file mounted, mount the soon to be created file
    if (!FileDataExists())
    {
      CreateFileData();
    }

    // If we are doing an auto check if we have to many
    if (autosave)
    {
      // first of all, if m_MaxAutosaveCount <= 0, then no autosaving
      // should occur at all
      if (s_MaxAutoSaveCount <= 0)
        return;

      // now, if the autosave count is at its limit, then we should
      // get rid of the oldest autosave
      if (m_MountedFileData.m_AutoSaves.Count >= s_MaxAutoSaveCount)
      {
        m_MountedFileData.m_AutoSaves.RemoveAt(0);
      }
    }
    else
    {
      // now, if the manual count is at its limit, then we should
      // get rid of the oldest save
      if (m_MountedFileData.m_ManualSaves.Count >= s_MaxManualSaveCount)
      {
        m_MountedFileData.m_ManualSaves.RemoveAt(0);
      }
    }

    // TODO, check if the auto save has diffrences from the last auto save, if not, discard save,
    if (hasDifferences)
    {
      // #6, 1, 3, 5, 8
      // We have data to add/overwite to any file
      levelData.m_TimeStamp = DateTime.Now;

      // Set the version of the save based off the last save
      List<LevelData> savesList = autosave ? m_MountedFileData.m_AutoSaves : m_MountedFileData.m_ManualSaves;
      if (savesList.Count > 0)
      {
        levelData.m_Version = savesList[^1].m_Version + 1;
      }
      else
      {
        levelData.m_Version = 1;
      }

      // If this is an auto save, store what version of the manual save we branched from to get these diffrences to save
      if (autosave)
      {
        // If we a auto saving to a temp file
        if (m_IsTempFile)
        {
          // We aren't diffing from a manual save, so version 0 means that
          levelData.m_BranchVersion = 0;
        }
        else
        {
          // Set the manual save version we are branching off from
          // Which will always be the latest manual save
          levelData.m_BranchVersion = m_MountedFileData.m_ManualSaves[^1].m_Version;
        }
      }

      savesList.Add(levelData);
    }
    else
    {
      // #2, 4
      // We have no changes to write, but we are writting to some file that isn't our own
      // So just copy our file to the destination file
      copyFile = true;
    }
    #endregion

    // If we have reach the max manual saves for the first time, give a warning that we will start to delete saves.
    if (s_MaxManualSaveCount == m_MountedFileData.m_ManualSaves[^1].m_Version)
    {
      Debug.Log("You have reached the maximum number of manual saves. " +
        "Any more saves on this save file will delete your oldest save to make room for you new saves.");
      // TODO, give warning popup
    }

    WriteMountedDataToFile(fullPath, overwriting, startTime, autosave, copyFile);
  }

  private void WriteMountedDataToFile(string fullPath, bool overwriting, DateTime startTime, bool autosave, bool copyFile)
  {
    try
    {
      if (copyFile)
      {
        File.Copy(m_MountedSaveFilePath, fullPath, true);
      }
      else
      {
        List<byte> data = new();
        data.AddRange(System.Text.Encoding.Default.GetBytes(JsonUtility.ToJson(m_MountedFileHeader) + "\n"));
        if (s_ShouldCompress)
          data.AddRange(StringCompression.Compress(JsonUtility.ToJson(m_MountedFileData)));
        else
          data.AddRange(System.Text.Encoding.Default.GetBytes(JsonUtility.ToJson(m_MountedFileData)));

        File.WriteAllBytes(fullPath, data.ToArray());
      }

      // If we did a manual save with a temp file, we no longer need the temp file.
      if (m_IsTempFile && !fullPath.Equals(m_MountedSaveFilePath))
      {
        File.Delete(m_MountedSaveFilePath);
        m_IsTempFile = false;
      }

      // Mount the file now that everything has been written
      MountFile(fullPath);

      // Don't add the temp file to history view
      if (!m_IsTempFile)
      {
        if (overwriting)
          m_MainThreadDispatcher.Enqueue(() => MoveHistoryItemToTop(m_SaveList, fullPath));
        else
          m_MainThreadDispatcher.Enqueue(() => AddHistoryItemForFile(m_SaveList, fullPath));
      }

      if (Application.platform == RuntimePlatform.WebGLPlayer)
        SyncFiles();

      var duration = DateTime.Now - startTime;
      var h = duration.Hours; // If this is greater than 0, we got beeg problems
      var m = duration.Minutes;
      var s = Math.Round(duration.TotalSeconds % 60.0, 2);

      var durationStr = "";
      if (h > 0)
        durationStr += $"{h}h ";
      if (m > 0)
        durationStr += $"{m}m ";
      durationStr += $"{s}s";

      var mainColor = "#ffffff99";
      var fileColor = autosave ? "white" : "yellow";
      var timeColor = "#ffffff66";
      m_MainThreadDispatcher.Enqueue(() =>
      StatusBar.Print($"<color={mainColor}>Saved</color> <color={fileColor}>{Path.GetFileName(fullPath)}</color> <color={timeColor}>in {durationStr}</color>"));
    }
    catch (Exception e)
    {
      var errorString = $"Error while saving. {e.Message} ({e.GetType()})";
      m_MainThreadDispatcher.Enqueue(() => StatusBar.Print(errorString));
      Debug.LogError(errorString);
    }
  }

  public void CancelOverwrite()
  {
    m_PendingSaveFullPath = "";
  }

  // Deprecated for now untill real use is found.
  // Will need update for save versioning.
  [Obsolete]
  public void CopyToClipboard()
  {
    if (GlobalData.AreEffectsUnderway())
      return;

    var jsonString = m_TileGrid.ToJsonString();
    // If we are useing a compression alg for loading/saving, copy this level as a copressed string
    //if (s_ShouldCompress)
    //jsonString = StringCompression.Compress(jsonString);

    var te = new TextEditor { text = jsonString };
    te.SelectAll();
    te.Copy();

    StatusBar.Print("Level copied to clipboard.");
  }

  // Deprecated for now untill real use is found.
  // Will need update for save versioning.
  [Obsolete]
  public void LoadFromClipboard()
  {
    if (GlobalData.AreEffectsUnderway())
      return;

    var te = new TextEditor { multiline = true };
    te.Paste();
    var text = te.text;

    if (string.IsNullOrEmpty(text))
    {
      StatusBar.Print("You tried to paste a level from the clipboard, but it's empty.");
    }
    else
    {
      //LoadFromJson(text);
    }
  }

  public void LoadVersion(int version)
  {
    if (!FileDataExists())
    {
      Debug.LogWarning($"No file loaded to load spicific version");
      return;
    }

    m_TileGrid.LoadFromDictonary(GetGridDictionaryFromLevelData(version));
  }

  public void LoadAutoSave(int version)
  {
    if (!FileDataExists())
    {
      Debug.LogWarning($"No file loaded to load spicific version");
      return;
    }
    if (!FindAutoSaveVersion(version, out LevelData autoSaveData))
      return;

    var grid = GetGridDictionaryFromLevelData(autoSaveData.m_BranchVersion);

    foreach (var tile in autoSaveData.m_AddedTiles)
    {
      grid[tile.m_GridIndex] = tile;
    }
    foreach (var pos in autoSaveData.m_RemovedTiles)
    {
      grid.Remove(pos);
    }

    m_TileGrid.LoadFromDictonary(grid);
  }

  public void LoadFromFullPath(string fullPath)
  {
    if (GlobalData.AreEffectsUnderway())
      return;

    try
    {
      MountFile(fullPath);
      LoadFromJson(File.ReadAllBytes(fullPath));
    }
    catch (Exception e)
    {
      // File not loaded, remove file mount
      UnmountFile();
      Debug.LogError($"Error while loading. {e.Message} ({e.GetType()})");
    }
  }

  public void LoadFromTextAsset(TextAsset level)
  {
    if (GlobalData.AreEffectsUnderway())
      return;

    // There is no file loaded from, so mount no files
    UnmountFile();
    LoadFromJson(level.bytes);
  }

  // Intermidiatarty load function. Calls the rest of the load functions.
  void LoadFromJson(byte[] json)
  {
    // Make sure we have file data for the load
    if (!FileDataExists())
      CreateFileData();

    try
    {
      // Read the header first
      // The header is always uncompressed, and the data might be
      bool splitError = SplitNewLineBytes(json, out byte[] headerBytes, out byte[] dataBytes);

      if (splitError)
        throw new ArgumentException("Header and or Level data can not be found");

      JsonUtility.FromJsonOverwrite(System.Text.Encoding.Default.GetString(headerBytes), m_MountedFileHeader);

      // If the save file was made with a diffrent version
      if (!m_MountedFileHeader.m_BlbVersion.Equals(s_EditorVersion))
      {
        Debug.Log($"Save file {Path.GetFileName(m_MountedSaveFilePath)} was made with a diffrent BLB version. There may be possible errors.");
        m_MainThreadDispatcher.Enqueue(() =>
        StatusBar.Print($"Save file {Path.GetFileName(m_MountedSaveFilePath)} was made with a diffrent BLB version. There may be possible errors."));
        // TODO, should we return or keep going? Do we want to run a file with a diff version?
      }

      string data;

      // Decompress string if needed
      if (m_MountedFileHeader.m_IsDataCompressed)
        data = StringCompression.Decompress(dataBytes);
      else
        data = System.Text.Encoding.Default.GetString(dataBytes);

      JsonUtility.FromJsonOverwrite(data, m_MountedFileData);
    }
    catch (System.ArgumentException e)
    {
      Debug.Log($"Error loading save file {Path.GetFileName(m_MountedSaveFilePath)} : {e}");
      m_MainThreadDispatcher.Enqueue(() =>
      StatusBar.Print($"Error loading save file {Path.GetFileName(m_MountedSaveFilePath)} : {e}"));
      return;
    }

    m_TileGrid.LoadFromDictonary(GetGridDictionaryFromLevelData());
  }

  // Splits a byte array about a new line.
  // Returns true if the new line cant be found
  bool SplitNewLineBytes(in byte[] data, out byte[] left, out byte[] right)
  {
    left = new byte[0];
    int i;
    for (i = 0; i < data.Length; i++)
    {
      if (data[i] == '\n')
      {
        // copy all the data from 0 to i - 1 to the left buffer
        left = data.Take(i).ToArray();
        break;
      }
    }
    // copy all the data from i + 1 to data.Length - 1 to the right buffer
    right = data.Skip(i + 1).ToArray();

    if (left.Length == 0)
      return true;
    return false;
  }

  // Will convert the level data to a Dictionary of elements up to the passed in version
  // If no version is passed in, we will flatten to the latest version
  Dictionary<Vector2Int, TileGrid.Element> GetGridDictionaryFromLevelData(int version = int.MaxValue)
  {
    Dictionary<Vector2Int, TileGrid.Element> tiles = new();

    foreach (var levelData in m_MountedFileData.m_ManualSaves)
    {
      // Stop flattening the level once we pass the version we want
      if (levelData.m_Version > version)
        break;

      foreach (var tile in levelData.m_AddedTiles)
      {
        tiles[tile.m_GridIndex] = tile;
      }
      foreach (var pos in levelData.m_RemovedTiles)
      {
        tiles.Remove(pos);
      }
    }

    return tiles;
  }

  // Removes an auto save
  // Returns true if we were successful
  public bool DeleteAutoSave(int version)
  {
    if (!FileDataExists())
    {
      Debug.LogWarning($"No file loaded to delete autosave");
      return false;
    }

    for (int i = 0; i < m_MountedFileData.m_AutoSaves.Count; ++i)
    {
      if (m_MountedFileData.m_AutoSaves[i].m_Version == version)
      {
        m_MountedFileData.m_AutoSaves.RemoveAt(i);
        return true;
      }
    }

    Debug.LogWarning($"Couldn't find auto save version {version} to delete");
    return false;
  }

  // Will delete a manual verion
  public void DeleteLevelVersion(int version)
  {
    // The easiest way to delete a version is to flatten it with the next verion
    // If this is the newest verion then we can just delete it
    // Luckly FlattenRange will deal with the endVerion going past the list length

    if (!FileDataExists())
    {
      Debug.LogWarning($"No file loaded to delete version");
      return;
    }

    FlattenRange(version, version + 1);
  }

  // Take a range of two versions and flatten them down to one data verion
  // Can only flatten MANUAL versions
  // Care is taken if endVersion is past the latest verion
  public void FlattenRange(int startVersion, int endVersion)
  {
    // Invalid range
    if (startVersion > endVersion)
    {
      Debug.LogWarning($"Invalid level flatten range of {startVersion} to {endVersion}");
      return;
    }

    if (!FileDataExists())
    {
      Debug.LogWarning($"No file loaded to flatten range");
      return;
    }

    Dictionary<Vector2Int, TileGrid.Element> squashedLevelAdd = new();
    HashSet<Vector2Int> squashedLevelRemove = new();
    DateTime timeStamp = DateTime.Now;
    int lastVersion = startVersion;

    // Care is taken if the endVerion is outside of the list
    // So we keep track of the last versions, well, version number
    foreach (var levelData in m_MountedFileData.m_ManualSaves)
    {
      if (levelData.m_Version < startVersion)
        continue;

      // If we finished our version loop, break
      if (levelData.m_Version > endVersion)
        break;

      // Keep track of the last verions stats
      timeStamp = levelData.m_TimeStamp;
      lastVersion = levelData.m_Version;

      // Convert this version into the collected grid
      // We might not start at version 1, so we need to keep track of the removes as they record the removes from the prior version
      // However the next version might add a tile where we were going to do a remove, so we need to remove the removes when adding a tile
      foreach (var tile in levelData.m_AddedTiles)
      {
        // Add the tile to the list
        // If we had record to remove it earlier, remove the record
        if (squashedLevelRemove.Contains(tile.m_GridIndex))
          squashedLevelRemove.Remove(tile.m_GridIndex);
        squashedLevelAdd[tile.m_GridIndex] = tile;
      }
      foreach (var pos in levelData.m_RemovedTiles)
      {
        // Remove a tile if we have one
        // Else add it to a remove list
        if (squashedLevelAdd.ContainsKey(pos))
          squashedLevelAdd.Remove(pos);
        else
          squashedLevelRemove.Add(pos);
      }
    }

    // Removed squashed versions
    int index = 0;
    while (index < m_MountedFileData.m_ManualSaves.Count)
    {
      // Get i up to the start version
      if (m_MountedFileData.m_ManualSaves[index].m_Version < startVersion)
      {
        ++index;
        continue;
      }

      // Remove the indexs up to the end version
      if (m_MountedFileData.m_ManualSaves[index].m_Version <= endVersion)
      {
        m_MountedFileData.m_ManualSaves.RemoveAt(index);
        continue;
      }

      // If there are more versions but we removed all out indexes, break the loop
      break;
    }

    // At this point the endVersion data will exist, or never existed and everything after startVersion was deleted

    // If we have diffrences, add thoes combined diffs to the version list
    // The addition will replace the endVersion
    if (squashedLevelRemove.Count > 0 || squashedLevelAdd.Count > 0)
    {
      LevelData levelData = new()
      {
        m_Version = lastVersion,
        m_TimeStamp = timeStamp,
        m_AddedTiles = squashedLevelAdd.Values.ToList(),
        m_RemovedTiles = squashedLevelRemove.ToList()
      };

      // If we had no endVersion. Then add a new version at the end
      if (index > m_MountedFileData.m_ManualSaves.Count)
        m_MountedFileData.m_ManualSaves.Add(levelData);
      else
        m_MountedFileData.m_ManualSaves.Insert(index, levelData);
    }
  }

  bool FindAutoSaveVersion(int version, out LevelData levelData)
  {
    return SaveFinderHelper(version, true, out levelData);
  }

  bool FindManualSaveVersion(int version, out LevelData levelData)
  {
    return SaveFinderHelper(version, false, out levelData);
  }

  bool SaveFinderHelper(int version, bool autoSave, out LevelData levelData)
  {
    levelData = new();

    foreach (var data in autoSave ? m_MountedFileData.m_AutoSaves : m_MountedFileData.m_ManualSaves)
    {
      if (data.m_Version == version)
      {
        levelData = data;
        return true;
      }
    }
    return false;
  }

  void SetDirectoryName(string name)
  {
    if (GlobalData.AreEffectsUnderway())
      return;

    if (!ValidateDirectoryName(name))
    {
      // modal: something's wrong with the file name
      return;
    }

    var documentsPath = GetDocumentsPath();
    // this will never throw as long as s_RootDirectoryName is valid
    var newDirectoryPath = Path.Combine(documentsPath, s_RootDirectoryName, name);

    if (Directory.Exists(newDirectoryPath))
    {
      try
      {
        var filePaths = Directory.GetFiles(newDirectoryPath);
        var validFilePaths = filePaths.Where(path => Path.HasExtension(path)).ToArray();

        if (filePaths.Length == 0)
        {
          // modal: pointing to an existing empty folder
        }
        else if (validFilePaths.Length == 0)
        {
          // modal: this folder doesn't have any BLB level files in it
        }
        else
        {
          // modal: pointing to an existing folder with stuff in it
          m_SaveList.Clear();
          SortByDateModified(validFilePaths);

          // at this point, filePaths is already sorted chronologically
          AddHistoryItemsForFiles(validFilePaths);
        }
      }
      catch (Exception e)
      {
        // this probably can't happen, but....
        StatusBar.Print($"Error getting files in directory. {e.Message} ({e.GetType()})");
      }
    }
    else
    {
      // get down with your bad self
      Directory.CreateDirectory(newDirectoryPath);
    }

    m_CurrentDirectoryPath = newDirectoryPath;
  }


  public void ShowDirectoryInExplorer()
  {
    if (Application.platform == RuntimePlatform.WebGLPlayer)
      return;

    Application.OpenURL($"file://{m_CurrentDirectoryPath}");
  }


  void MoveHistoryItemToTop(UiListView historyList, string fullPath)
  {
    var item = historyList.GetItemByFullPath(fullPath);
    historyList.MoveToTop(item.transform);
  }

  void RemoveHistoryItem(UiListView historyList, string fullPath)
  {
    var element = historyList.GetItemByFullPath(fullPath);
    historyList.Remove(element.GetComponent<RectTransform>());
  }

  void AddHistoryItemForFile(UiListView historyList, string fullPath)
  {
    var fileName = Path.GetFileNameWithoutExtension(fullPath);
    var rt = AddHelper(fullPath, fileName);
    historyList.Add(rt);
  }


  void AddHistoryItemsForFiles(string[] fullPaths)
  {
    if (GlobalData.AreEffectsUnderway())
      return;

    var listItems = new List<RectTransform>();

    foreach (var fullPath in fullPaths)
    {
      // Path.GetFileNameWithoutExtension can only throw ArgumentException
      // for the path having invalid characters, and AddHelper will only be
      // called after ValidateDirectoryName has cleared the path
      var fileName = Path.GetFileNameWithoutExtension(fullPath);

      var rt = AddHelper(fullPath, fileName);
      listItems.Add(rt);
    }

    m_SaveList.Add(listItems);
  }


  RectTransform AddHelper(string fullPath, string fileName)
  {
    var listItem = Instantiate(m_HistoryItemPrefab);

    listItem.Setup(this, fullPath, fileName);
    var rt = listItem.GetComponent<RectTransform>();

    return rt;
  }

  bool ValidateDirectoryName(string directoryName)
  {
    if (string.IsNullOrEmpty(directoryName) || string.IsNullOrWhiteSpace(directoryName))
      return false;

    var invalidChars = Path.GetInvalidPathChars();

    if (invalidChars.Length > 0)
      return directoryName.IndexOfAny(invalidChars) < 0;
    else
      return true;
  }

  string GetDocumentsPath()
  {
    try
    {
      switch (Application.platform)
      {
        case RuntimePlatform.OSXEditor:
        case RuntimePlatform.OSXPlayer:
        case RuntimePlatform.WindowsPlayer:
        case RuntimePlatform.WindowsEditor:
        case RuntimePlatform.LinuxPlayer:
        case RuntimePlatform.LinuxEditor:
        case RuntimePlatform.WSAPlayerX86:
        case RuntimePlatform.WSAPlayerX64:
        case RuntimePlatform.WSAPlayerARM:
          return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        default:
          return Application.persistentDataPath;
      }
    }
    catch (Exception e)
    {
      Debug.LogError($"Error getting document path. Defaulting to persistent data path. {e.Message} ({e.GetType()})");

      return Application.persistentDataPath;
    }
  }

  string CreateTempFileName()
  {
    return Path.Combine(m_CurrentDirectoryPath, Guid.NewGuid().ToString() + ".blb");
  }

  void SortByDateTimeParsedFileNames(string[] files)
  {
    Array.Sort(files, FileNameComparison);
  }

  void SortByDateModified(string[] files)
  {
    Array.Sort(files, DateModifiedComparison);
  }

  static int FileNameComparison(string a, string b)
  {
    var dateTimeA = GetDateTimeFromFileName(a);
    var dateTimeB = GetDateTimeFromFileName(b);

    return DateTime.Compare(dateTimeA, dateTimeB);
  }

  static int DateModifiedComparison(string a, string b)
  {
    var dateTimeA = File.GetLastWriteTime(a);
    var dateTimeB = File.GetLastWriteTime(b);

    return DateTime.Compare(dateTimeA, dateTimeB);
  }

  static DateTime GetDateTimeFromFileName(string fileName)
  {
    var withoutExtension = Path.GetFileNameWithoutExtension(fileName);
    var firstSpaceIndex = withoutExtension.IndexOf(' ');
    string dateTimeString = withoutExtension.Remove(0, firstSpaceIndex + 1);

    var output = DateTime.Now;

    try
    {
      output = DateTime.ParseExact(dateTimeString, s_DateTimeFormat, CultureInfo.InvariantCulture);
    }
    catch (FormatException e)
    {
      Debug.LogError($"Error parsing the DateTime of {fileName}. Defaulting to DateTime.Now. {e.Message}");
    }

    return output;
  }

  protected class StringCompression
  {
    // Compresses the input data using GZip
    public static byte[] Compress(string input)
    {
      byte[] byteArray = System.Text.Encoding.Default.GetBytes(input);

      using MemoryStream ms = new();
      using (GZipStream sw = new(ms, CompressionMode.Compress))
      {
        sw.Write(byteArray, 0, byteArray.Length);
      }
      return ms.ToArray();
    }

    // Decompresses the input data using GZip
    public static string Decompress(byte[] compressedData)
    {
      using MemoryStream ms = new(compressedData);
      using GZipStream sr = new(ms, CompressionMode.Decompress);
      using StreamReader reader = new(sr);
      return reader.ReadToEnd();
    }
  }

  // A list of actions in a queue for use by threads to store unity internal related functions that must be run on the main thread.
  public class MainThreadDispatcher
  {
    private static readonly object s_LockObject = new();
    private Queue<System.Action> m_ActionQueue = new();

    // Runs all the Queued up actions in the list
    // Run in a place where only the main thread is run.
    public void Update()
    {
      lock (s_LockObject)
      {
        // Execute all queued actions on the main thread
        while (m_ActionQueue.Count > 0)
        {
          System.Action action = m_ActionQueue.Dequeue();
          action.Invoke();
        }
      }
    }

    // Add a function call to the queue
    public void Enqueue(System.Action action)
    {
      lock (s_LockObject)
      {
        // Enqueue the action to be executed on the main thread
        m_ActionQueue.Enqueue(action);
      }
    }
  }
}

// TODO, make new level button
// TODO, Star on file name to show unsaved changes.

// Extra credit
// TODO, fix area placement taking forever
// TODO, Large level creation and deletion still takes a long time.
// TODO, Make RequestDialogAtCenterWithStrings an async function that waits for the user to finish the dialog then continue.


// These three are the same:
// TODO, game exit or file load "Are you sure" when there are unsaved changes or no file is mounted
// TODO, add are you sure, if you load a level with unsaved changes.
// TODO, When closeing app, ask to save if there are unsaved changes.
// If no, delete temp file on close


// __Needs UI__
// TODO, make new level button
// TODO, add feature to select a range of versions and squash them together
// TODO, right click version to delete auto or manual save
// Unsaved changes prompt
// Warning about max manual saves reached
// Ability to load auto save


// Auto versions will not be allowed to flatten as they are all based off the manual saves.
// If they want an auto save to be perminant, then load it and save.
// Should they be allowed to delete auto saves?
// Yes, because if they want to save memory
// No, because the will be deleted eventualy...
// If yes, then should we have a "delete all auto saves" button?




