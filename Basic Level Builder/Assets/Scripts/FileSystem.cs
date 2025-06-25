/***************************************************
Authors:        Douglas Zwick, Brenden Epp
Last Updated:   3/24/2025

Copyright 2018-2025, DigiPen Institute of Technology
***************************************************/

using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Runtime.InteropServices;
using B83.Win32;
using System.Threading;
using System.Text.RegularExpressions;

public class FileSystem : MonoBehaviour
{
  #region singleton
  private static FileSystem _instance;
  public static FileSystem Instance
  {
    get
    {
      if (_instance == null)
      {
        _instance = FindObjectOfType<FileSystem>();
      }
      return _instance;
    }
  }

  private void Awake()
  {
    if (_instance != null && _instance != this)
    {
      Destroy(this.gameObject);
    }
    else
    {
      _instance = this;
    }
  }
  #endregion

  readonly static public string s_FilenameExtension = ".blb";
  readonly static public string s_RootDirectoryName = "Basic Level Builder";
  readonly static public string s_DateTimeFormat = "h-mm-ss.ff tt, ddd d MMM yyyy";
  readonly static private string s_AutoSaveName = "Auto";
  readonly static private string s_ManualSaveName = "Version ";
  readonly static public int s_MaxAutoSaveCount = 20;
  readonly static public int s_MaxManualSaveCount = 100;
  readonly static bool s_ShouldCompress = false;
  static string s_EditorVersion;

  public string m_DefaultDirectoryName = "Default Project";
  public UiSaveFileItem m_FileItemPrefab;
  public UiListView m_SaveList;
  public TileGrid m_TileGrid;

  UnityDragAndDropHook m_DragAndDropHook;

  ModalDialogMaster m_ModalDialogMaster;
  [SerializeField]
  ModalDialogAdder m_OverrideDialogAdder;
  [SerializeField]
  ModalDialogAdder m_SaveAsDialogAdder;
  [SerializeField]
  ModalDialogAdder m_ExportAsDialogAdder;

  string m_CurrentDirectoryPath;
  string m_PendingSaveFullPath = "";
  LevelData m_PendingExportLevelData = null;

  FileInfo m_MountedFileInfo;

  // The version of the manual or autosave that is loaded
  Version m_loadedVersion;

  // A thread to run when saving should be performed.
  // Only one save thread is run at once.
  private Thread m_SavingThread;
  // A queue of events that the saving thread will enqueue for the main thread
  private readonly MainThreadDispatcher m_MainThreadDispatcher = new();

  [DllImport("__Internal")]
  private static extern void SyncFiles();

  #region FileStructure classes

  public struct FileInfo
  {
    public bool m_IsTempFile;
    public string m_SaveFilePath;
    public FileData m_FileData;
    public Header m_FileHeader;
  }

  [Serializable]
  public struct JsonDateTime
  {
    public long value;
    public static implicit operator DateTime(JsonDateTime jdt)
    {
      return DateTime.FromFileTime(jdt.value);
    }
    public static implicit operator JsonDateTime(DateTime dt)
    {
      JsonDateTime jdt = new();
      jdt.value = dt.ToFileTime();
      return jdt;
    }
  }

  [Serializable]
  public class FileData
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
  public class Header
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
  public struct Version
  {
    public Version(int manual, int Auto)
    {
      m_ManualVersion = manual;
      m_AutoVersion = Auto;
    }

    public readonly bool IsManual()
    {
      return m_AutoVersion == 0;
    }

    public override readonly string ToString()
    {
      return $"Save version: Manual {m_ManualVersion}, Auto {m_AutoVersion}"; // Using string interpolation for a readable output
    }

    public readonly bool Equals(Version rhs)
    {
      return m_ManualVersion == rhs.m_ManualVersion && m_AutoVersion == rhs.m_AutoVersion;
    }

    public static bool operator ==(Version left, Version right)
    {
      return left.Equals(right); // Delegate to Equals method
    }

    public static bool operator !=(Version left, Version right)
    {
      return !(left == right);
    }

    public override bool Equals(object obj)
    {
      return obj is Version other && Equals(other);
    }

    // Override GetHashCode
    public override int GetHashCode()
    {
      return m_ManualVersion.GetHashCode() + m_AutoVersion.GetHashCode();
    }

    public readonly int CompareTo(Version other)
    {
      // Sorts Largest to Smallest/Top to Bottom
      // -# = This goes up
      // +# = This goes down
      // == This stays

      int diff = other.m_ManualVersion - m_ManualVersion;

      // If they are the same maunal save, one (or both) of them is an autosave.
      if (diff == 0)
      {
        // Sort the auto saves to have the newest on top
        diff = other.m_AutoVersion - m_AutoVersion;

        // If either werer a manaul save, we need to put that on top
        if (other.m_AutoVersion == 0)
          diff = 1;
        if (m_AutoVersion == 0)
          diff = -1;
      }

      return diff;
    }

    // The version of the manaul save, or maunal the auto is branched off of
    public int m_ManualVersion;
    // The autosave version, 0 if not an autosave
    public int m_AutoVersion;
  }

  [Serializable]
  public class LevelData
  {
    public LevelData()
    {
      m_AddedTiles = new List<TileGrid.Element>();
      m_RemovedTiles = new List<Vector2Int>();
    }

    public Version m_Version;
    public string m_Name;
    public JsonDateTime m_TimeStamp;
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
      if (IsFileMounted() && !FileExists(m_MountedFileInfo.m_SaveFilePath))
      {
        var errorString = $"Error: File with path \"{m_MountedFileInfo.m_SaveFilePath}\" could not be found. " +
               "Loaded level has been saved with the same name.";
        var tempPath = Path.GetFileNameWithoutExtension(m_MountedFileInfo.m_SaveFilePath);
        Debug.LogWarning(errorString);

        UnmountFile();
        SaveAs(tempPath, false);

        StatusBar.Print(errorString);
      }

      m_SaveList.ValidateAllItems();
    }
  }

  /// <summary>
  /// Creates new file data structures.
  /// </summary>
  public static void CreateFileInfo(out FileInfo fileInfo, string filePath = "")
  {
    fileInfo = new()
    {
      m_SaveFilePath = filePath,
      m_FileHeader = new(s_EditorVersion, s_ShouldCompress),
      m_FileData = new()
    };
  }

  /// <summary>
  /// Clears the file data structures.
  /// </summary>
  void ClearFileData(FileInfo fileInfo)
  {
    fileInfo.m_FileHeader = null;
    fileInfo.m_FileData = null;
  }

  /// <summary>
  /// Checks if file data exists.
  /// </summary>
  /// <returns>True if file data exists, false otherwise</returns>
  bool FileDataExists(FileData fileData)
  {
    return fileData != null;
  }

  /// <summary>
  /// Unmounts the current file.
  /// </summary>
  void UnmountFile()
  {
    m_MountedFileInfo.m_SaveFilePath = "";
  }

  /// <summary>
  /// Mounts a file at the specified path.
  /// </summary>
  /// <param name="filePath">The path to the file to mount</param>
  void MountFile(string filePath, FileInfo fileInfo)
  {
    m_MountedFileInfo = fileInfo;
    m_MountedFileInfo.m_SaveFilePath = filePath;
  }

  bool FileExists(string filePath)
  {
    return !String.IsNullOrEmpty(filePath) && File.Exists(filePath);
  }

  bool IsFileMounted()
  {
    return !String.IsNullOrEmpty(m_MountedFileInfo.m_SaveFilePath);
  }

  public void ExportVersion(string sourcePath, Version version)
  {
    // Gather the level data to export
    GetDataFromFullPath(sourcePath, out FileInfo sourceFileInfo);

    GetVersionLevelData(sourceFileInfo.m_FileData, version, out m_PendingExportLevelData);
    // Set the data to be the first version of this file.
    m_PendingExportLevelData.m_Version = new(1, 0);
    m_PendingExportLevelData.m_AddedTiles = new List<TileGrid.Element>(GetGridDictionaryFromFileData(sourceFileInfo.m_FileData, version).Values);
    // If the name was an auto generated name, update it to be the first version name
    if (Regex.IsMatch(m_PendingExportLevelData.m_Name, $"^{Regex.Escape(s_ManualSaveName)}([0-9]{{1,3}})$"))
    {
      m_PendingExportLevelData.m_Name = s_ManualSaveName + "1";
    }

    // Call dialogue to get export file name
    m_ExportAsDialogAdder.RequestDialogsAtCenterWithStrings();

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

  /// <summary>
  /// Performs a manual save of the current level.
  /// </summary>
  public void ManualSave()
  {
    Save(false);
  }

  /// <summary>
  /// Performs an automatic save of the current level.
  /// </summary>
  public void Autosave()
  {
    Save(true);
  }

  public void SaveAs(string name, bool shouldPrintElapsedTime = true)
  {
    Save(false, name, shouldPrintElapsedTime);
  }

  void Save(bool autosave, string saveAsFileName = null, bool shouldPrintElapsedTime = true)
  {
    if (GlobalData.AreEffectsUnderway())
      return;

    // If we have a thread running
    if (m_SavingThread != null && m_SavingThread.IsAlive)
      return;

    if (!FileDataExists(m_MountedFileInfo.m_FileData))
    {
      CreateFileInfo(out m_MountedFileInfo);
    }

    string destFilePath;
    // If we are doing a SAVE AS
    if (saveAsFileName != null)
    {
      destFilePath = Path.Combine(m_CurrentDirectoryPath, saveAsFileName + s_FilenameExtension);

      // Give prompt if we are going to write to and existing file
      if (File.Exists(destFilePath))
      {
        m_PendingSaveFullPath = destFilePath;

        m_OverrideDialogAdder.RequestDialogsAtCenterWithStrings(Path.GetFileName(destFilePath));
        return;
      }
    }
    else
    {
      if (IsFileMounted())
      {
        // If we are doing a save, but we only have a temp file
        if (m_MountedFileInfo.m_IsTempFile && !autosave)
        {
          // request a name for a new file to save to
          m_SaveAsDialogAdder.RequestDialogsAtCenterWithStrings();
          return;
        }
        else
        {
          destFilePath = m_MountedFileInfo.m_SaveFilePath;

          // If our file is deleted/missing
          if (!File.Exists(m_MountedFileInfo.m_SaveFilePath))
          {
            // Because of the file validation on application focus, this SHOULD never happen.
            // But to be safe incase the file is deleted while playing the game, do this
            var errorString = $"Error: File with path \"{m_MountedFileInfo.m_SaveFilePath}\" could not be found." + Environment.NewLine +
              "A new file has been made for this save.";
            StatusBar.Print(errorString);
            Debug.LogWarning(errorString);
            RemoveFileItem(m_SaveList, m_MountedFileInfo.m_SaveFilePath);
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
          destFilePath = CreateTempFileName();
          m_MountedFileInfo.m_IsTempFile = true;
          // Mount temp file so we can check when the full file path isn't the temp file
          MountFile(destFilePath, m_MountedFileInfo);
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

    StartSavingThread(destFilePath, autosave, saveAsFileName != null, shouldPrintElapsedTime);
  }

  public void ConfirmOverwrite()
  {
    // Check if we were doing a SaveAs or an Export
    // If the Export data is empty, then we are doing a SaveAs
    if (m_PendingExportLevelData == null)
    {
      StartSavingThread(m_PendingSaveFullPath, false);
    }
    else
    {
      StartExportSavingThread(m_PendingSaveFullPath);
    }
    m_PendingSaveFullPath = "";
  }

  void StartSavingThread(string destFilePath, bool autosave, bool isSaveAs = false, bool shouldPrintElapsedTime = true)
  {
    // Copy the map data into a buffer to use for the saving thread.
    m_TileGrid.CopyGridBuffer();

    // Define parameters for the branched thread function
    object[] parameters = { destFilePath, autosave, shouldPrintElapsedTime };

    // Create a new thread and pass the ParameterizedThreadStart delegate
    if (isSaveAs)
      m_SavingThread = new Thread(new ParameterizedThreadStart(SavingThreadFlatten));
    else
      m_SavingThread = new Thread(new ParameterizedThreadStart(SavingThread));

    m_SavingThread.Start(parameters);
  }

  public void TryStartExportSavingThread(string fileName)
  {
    string destFilePath = Path.Combine(m_CurrentDirectoryPath, fileName + s_FilenameExtension);

    // Give prompt if we are going to write to and existing file
    if (File.Exists(destFilePath))
    {
      m_PendingSaveFullPath = destFilePath;

      m_OverrideDialogAdder.RequestDialogsAtCenterWithStrings(Path.GetFileName(destFilePath));
      return;
    }

    StartExportSavingThread(destFilePath);
  }

  private void StartExportSavingThread(string destFilePath)
  {
    m_SavingThread = new Thread(new ParameterizedThreadStart(ExportSavingThread));

    m_SavingThread.Start(destFilePath);
  }

  void SavingThreadFlatten(object threadParameters)
  {
    var startTime = DateTime.Now;

    // Extract the parameters from the object array
    object[] parameters = (object[])threadParameters;

    // Access the parameters
    string destFilePath = (string)parameters[0];
    bool isOverwriting = File.Exists(destFilePath);

    // Create new file date to clear out the old and only write in the current tile grid
    m_MountedFileInfo.m_FileData = new();

    m_TileGrid.GetLevelData(out LevelData levelData);

    levelData.m_TimeStamp = DateTime.Now;
    levelData.m_Version = new(1, 0);
    levelData.m_Name = s_ManualSaveName + "1";

    m_MountedFileInfo.m_FileData.m_ManualSaves.Add(levelData);

    try
    {
      bool shouldMountSave = true;
      bool isAutosave = false;
      bool shouldCopyFile = false;
      bool shouldPrintElapsedTime = (bool)parameters[2];
      WriteDataToFile(destFilePath, m_MountedFileInfo, shouldMountSave, isOverwriting, startTime, isAutosave, shouldCopyFile, shouldPrintElapsedTime);
    }
    catch (Exception e)
    {
      var errorString = $"Error while flattening and saving file: {e.Message} ({e.GetType()})";
      m_MainThreadDispatcher.Enqueue(() => StatusBar.Print(errorString));
      Debug.LogError(errorString);
    }
  }

  void SavingThread(object threadParameters)
  {
    var startTime = DateTime.Now;

    // Extract the parameters from the object array
    object[] parameters = (object[])threadParameters;

    // Access the parameters
    string destFilePath = (string)parameters[0];
    bool autosave = (bool)parameters[1];
    bool shouldPrintElapsedTime = (bool)parameters[2];
    bool overwriting = File.Exists(destFilePath);
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
    bool hasDifferences = GetDifferences(out LevelData levelData, m_MountedFileInfo);

    // If we are writting to our own file yet we have no changes, skip the save
    // Or we are writting to a temp file with no changes, ignore write
    if (overwriting && FileExists(m_MountedFileInfo.m_SaveFilePath) && destFilePath.Equals(m_MountedFileInfo.m_SaveFilePath) && !hasDifferences)
    {
      // #7
      var errorString = "Skipped save because there is nothing new to save";
      m_MainThreadDispatcher.Enqueue(() => StatusBar.Print(errorString));
      Debug.Log(errorString);
      return;
    }

    // TODO, see where we need to set and reset the m_MountedfileData
    // If we don't have a file mounted, mount the soon to be created file
    // TODO Maybe make a lock here incase two threads do it
    if (!FileDataExists(m_MountedFileInfo.m_FileData))
    {
      Debug.LogError("Damn, This should not happen. Check why file data doesn't exist here.");
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
      if (m_MountedFileInfo.m_FileData.m_AutoSaves.Count >= s_MaxAutoSaveCount)
      {
        m_MountedFileInfo.m_FileData.m_AutoSaves.RemoveAt(0);
      }
    }
    else
    {
      // now, if the manual count is at its limit, then we should
      // get rid of the oldest save
      if (m_MountedFileInfo.m_FileData.m_ManualSaves.Count >= s_MaxManualSaveCount)
      {
        m_MountedFileInfo.m_FileData.m_ManualSaves.RemoveAt(0);
      }
    }

    // TODO, check if the auto save has diffrences from the last auto save, if not, discard save,
    // TODO, check if manual save is the same as the last auto save, if so just move auto to manual
    // Only write if we have diffrences, or we have no user created file yet
    if (hasDifferences || m_MountedFileInfo.m_IsTempFile)
    {
      // #6, 1, 3, 5, 8
      // We have data to add/overwite to any file
      levelData.m_TimeStamp = DateTime.Now;

      // Manual
      if (!autosave)
      {
        if (m_MountedFileInfo.m_FileData.m_ManualSaves.Count > 0)
        {
          levelData.m_Version = new(m_MountedFileInfo.m_FileData.m_ManualSaves[^1].m_Version.m_ManualVersion + 1, 0);
        }
        else
        {
          levelData.m_Version = new(1, 0);
        }

        // Set the name of the version to just the version
        levelData.m_Name = s_ManualSaveName + levelData.m_Version.m_ManualVersion;

        m_MountedFileInfo.m_FileData.m_ManualSaves.Add(levelData);
      }
      // If this is an auto save, store what version of the manual save we branched from to get these diffrences to save
      else
      {
        // If we a auto saving to a temp file
        if (m_MountedFileInfo.m_IsTempFile)
        {
          // We aren't diffing from a manual save, but we list auto as 1 anyway since a val of 0 is treated as a manual save
          levelData.m_Version = new(1, 1);
        }
        else
        {
          // Set the manual save version we are branching off from
          // Get the manual version or the branched manual version if we loaded an auto save
          levelData.m_Version.m_ManualVersion = m_loadedVersion.m_ManualVersion;

          // Check if there are other autosaves branched from this manual
          // If so, our version will be 1 more than the newest one
          int lastVersion = GetLastAutoSaveVersion(m_MountedFileInfo.m_FileData, m_loadedVersion.m_ManualVersion);
          levelData.m_Version.m_AutoVersion = lastVersion + 1;
        }

        // Overwrite name for autosaves
        //levelData.m_Name = s_AutoSaveName;
        // TODO: Remove this debug line and re add previous
        levelData.m_Name = s_AutoSaveName + " " + levelData.m_Version;

        m_MountedFileInfo.m_FileData.m_AutoSaves.Add(levelData);
      }
    }
    else
    {
      // #2, 4
      // We have no changes to write, but we are writting to some file that isn't our own
      // So just copy our file to the destination file
      copyFile = true;
    }
    #endregion Add level changes to level data

    // If we have reach the max manual saves for the first time, give a warning that we will start to delete saves.
    if (m_MountedFileInfo.m_FileData.m_ManualSaves.Count > s_MaxManualSaveCount)
    {
      Debug.Log("You have reached the maximum number of saves. " +
        "Any more saves on this save file will delete your oldest save to make room for you new saves.");
      // TODO, give warning popup
    }
    try
    {
      bool shouldMountSave = true;
      WriteDataToFile(destFilePath, m_MountedFileInfo, shouldMountSave,
      overwriting, startTime, autosave, copyFile, shouldPrintElapsedTime);

      // If we are saving to the file we have mounted, set our loaded version to that new save
      if (m_MountedFileInfo.m_SaveFilePath == destFilePath || shouldMountSave)
      {
        m_loadedVersion = levelData.m_Version;
      }
    }
    catch (Exception e)
    {
      var errorString = $"Error while saving file: {e.Message} ({e.GetType()})";
      m_MainThreadDispatcher.Enqueue(() => StatusBar.Print(errorString));
      Debug.LogError(errorString);
    }
  }

  void ExportSavingThread(object threadParameter)
  {
    var startTime = DateTime.Now;

    // Extract the parameters from the object array
    string destFilePath = (string)threadParameter;

    bool isOverwriting = File.Exists(destFilePath);

    CreateFileInfo(out FileInfo sourceInfo, destFilePath);

    sourceInfo.m_FileData.m_ManualSaves.Add(m_PendingExportLevelData);

    // Null out data so we know if we finished our export
    m_PendingExportLevelData = null;

    try
    {
      bool shouldMountSave = false;
      bool isAutosave = false;
      bool shouldCopyFile = false;
      bool shouldPrintElapsedTime = true;
      WriteDataToFile(destFilePath, sourceInfo, shouldMountSave, isOverwriting, startTime, isAutosave, shouldCopyFile, shouldPrintElapsedTime);
    }
    catch (Exception e)
    {
      var errorString = $"Error while exporting and saving file: {e.Message} ({e.GetType()})";
      m_MainThreadDispatcher.Enqueue(() => StatusBar.Print(errorString));
      Debug.LogError(errorString);
    }
  }

  // Make sure m_TileGrid.CopyGridBuffer is called before hand
  private bool GetDifferences(out LevelData diffrences, FileInfo file, Version? version = null)
  {
    diffrences = new();

    Dictionary<Vector2Int, TileGrid.Element> oldGrid = GetGridDictionaryFromFileData(file.m_FileData, version);

    bool hasDiffrences = false;

    foreach (var kvp in m_TileGrid.GetGridBuffer())
    {
      Vector2Int position = kvp.Key;
      TileGrid.Element currentElement = kvp.Value;

      if (oldGrid.TryGetValue(position, out TileGrid.Element oldElement))
      {
        bool same = currentElement.Equals(oldElement);

        // Removed element so we don't check it again in the next loop
        oldGrid.Remove(position);

        if (same)
          continue;
      }
      diffrences.m_AddedTiles.Add(currentElement);
      hasDiffrences = true;
    }

    // Every tile left in the old grid will be removed
    foreach (var kvp in oldGrid)
    {
      diffrences.m_RemovedTiles.Add(kvp.Key);
      hasDiffrences = true;
    }

    return hasDiffrences;
  }

  private void UpdateFileToItemList(string fullPath, bool overwriting)
  {
    if (overwriting)
      m_MainThreadDispatcher.Enqueue(() => MoveFileItemToTop(m_SaveList, fullPath));
    else
      m_MainThreadDispatcher.Enqueue(() => AddFileItemForFile(m_SaveList, fullPath));
  }

  /// <summary>
  /// Copies a file from the source file info to the destination path.
  /// </summary>
  /// <exception cref="Exception">Thrown when an error occurs during the copy operation.</exception>
  private void CopyFile(string destFilePath, FileInfo sourceFileInfo)
  {
    File.Copy(sourceFileInfo.m_SaveFilePath, destFilePath, true);
  }

  /// <summary>
  /// Writes data to a file with additional operations like mounting and UI updates.
  /// </summary>
  /// <param name="destFilePath">The destination file path.</param>
  /// <param name="sourceFileInfo">The source file info.</param>
  /// <param name="shouldMountSave">Whether to mount the save after writing.</param>
  /// <param name="isOverwriting">Whether the operation is overwriting an existing file.</param>
  /// <param name="startTime">The start time of the operation for duration calculation.</param>
  /// <param name="isAutosave">Whether the operation is an autosave.</param>
  /// <param name="shouldCopyFile">Whether to copy the file instead of writing data.</param>
  /// <param name="shouldPrintElapsedTime">Whether to print the elapsed time.</param>
  /// <exception cref="Exception">Thrown when an error occurs during file operations.</exception>
  private void WriteDataToFile(string destFilePath, FileInfo sourceFileInfo, bool shouldMountSave,
    bool isOverwriting, DateTime startTime, bool isAutosave, bool shouldCopyFile, bool shouldPrintElapsedTime = true)
  {
    if (shouldCopyFile)
    {
      CopyFile(destFilePath, sourceFileInfo);
    }
    else
    {
      WriteDataToFile(destFilePath, sourceFileInfo);
    }

    // If we did a manual save with a temp file, we no longer need the temp file.
    if (sourceFileInfo.m_IsTempFile && !destFilePath.Equals(m_MountedFileInfo.m_SaveFilePath))
    {
      File.Delete(sourceFileInfo.m_SaveFilePath);
      sourceFileInfo.m_IsTempFile = false;
    }

    // If we aren't saving to a temp file
    if (!sourceFileInfo.m_IsTempFile)
    {
      UpdateFileToItemList(destFilePath, isOverwriting);
    }

    if (Application.platform == RuntimePlatform.WebGLPlayer)
      SyncFiles();

    if (shouldMountSave)
      MountFile(destFilePath, sourceFileInfo);

    if (shouldPrintElapsedTime)
    {
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
      var fileColor = isAutosave ? "white" : "yellow";
      var timeColor = "#ffffff66";
      m_MainThreadDispatcher.Enqueue(() =>
      StatusBar.Print($"<color={mainColor}>Saved</color> <color={fileColor}>{Path.GetFileName(destFilePath)}</color> <color={timeColor}>in {durationStr}</color>"));
    }
  }

  /// <summary>
  /// Writes data to a file.
  /// </summary>
  /// <param name="destFilePath">The destination file path.</param>
  /// <param name="sourceFileInfo">The source file info.</param>
  /// <exception cref="Exception">Thrown when an error occurs during file operations.</exception>
  private void WriteDataToFile(string destFilePath, FileInfo sourceFileInfo)
  {
    List<byte> data = new();
    data.AddRange(System.Text.Encoding.Default.GetBytes(JsonUtility.ToJson(sourceFileInfo.m_FileHeader) + "\n"));
    if (s_ShouldCompress)
      data.AddRange(StringCompression.Compress(JsonUtility.ToJson(sourceFileInfo.m_FileData)));
    else
      data.AddRange(System.Text.Encoding.Default.GetBytes(JsonUtility.ToJson(sourceFileInfo.m_FileData)));

    File.WriteAllBytes(destFilePath, data.ToArray());
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

  /// <summary>
  /// Gets data from a file at the specified path.
  /// </summary>
  /// <param name="fullPath">The full path to the file.</param>
  /// <param name="fileInfo">The file info to populate.</param>
  /// <exception cref="Exception">Thrown when the file cannot be found.</exception>
  public void GetDataFromFullPath(string fullPath, out FileInfo fileInfo)
  {
    CreateFileInfo(out fileInfo, fullPath);
    if (!File.Exists(fullPath))
    {
      throw new Exception($"File not found: {fullPath}");
    }

    GetDataFromJson(File.ReadAllBytes(fullPath), fileInfo);
  }

  public void LoadFromFullPath(string fullPath, Version? version = null)
  {
    if (GlobalData.AreEffectsUnderway())
      return;

    try
    {
      if (!FileDataExists(m_MountedFileInfo.m_FileData))
        CreateFileInfo(out m_MountedFileInfo);

      MountFile(fullPath, m_MountedFileInfo);
      LoadFromJson(File.ReadAllBytes(fullPath), version);

      m_loadedVersion = version ?? new(GetLastManualSaveVersion(m_MountedFileInfo.m_FileData), 0);
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
    try
    {
      LoadFromJson(level.bytes);
    }
    catch (Exception e)
    {
      Debug.LogError($"Error while loading. {e.Message} ({e.GetType()})");
    }
  }

  // Intermidiatarty load function. Calls the rest of the load functions.
  void LoadFromJson(byte[] json, Version? version = null)
  {
    // Make sure we have file data for the load
    if (!FileDataExists(m_MountedFileInfo.m_FileData))
      CreateFileInfo(out m_MountedFileInfo);

    GetDataFromJson(json, m_MountedFileInfo);

    m_TileGrid.LoadFromDictonary(GetGridDictionaryFromFileData(m_MountedFileInfo.m_FileData, version));
  }

  /// <summary>
  /// Grabs data from file and stores it in passed in FileData and a Header.
  /// </summary>
  /// <param name="json">The JSON data as bytes.</param>
  /// <param name="fileInfo">The file info to populate.</param>
  /// <exception cref="FormatException">Thrown when the file format is invalid.</exception>
  void GetDataFromJson(byte[] json, FileInfo fileInfo)
  {
    // Read the header first
    // The header is always uncompressed, and the data might be
    byte[] headerBytes;
    byte[] dataBytes;

    try
    {
      SplitNewLineBytes(json, out headerBytes, out dataBytes);
    }
    catch (FormatException e)
    {
      throw new FormatException("Header and/or level data cannot be found", e);
    }

    JsonUtility.FromJsonOverwrite(System.Text.Encoding.Default.GetString(headerBytes), fileInfo.m_FileHeader);

    // If the save file was made with a diffrent version
    if (!fileInfo.m_FileHeader.m_BlbVersion.Equals(s_EditorVersion))
    {
      string errorStr = $"Save file {Path.GetFileName(fileInfo.m_SaveFilePath)} was made with a different BLB version. There may be possible errors.";
      Debug.Log(errorStr);
      m_MainThreadDispatcher.Enqueue(() => StatusBar.Print(errorStr));
      // TODO, should we return or keep going? Do we want to run a file with a diff version?
    }

    string data;

    // Decompress string if needed
    if (fileInfo.m_FileHeader.m_IsDataCompressed)
      data = StringCompression.Decompress(dataBytes);
    else
      data = System.Text.Encoding.Default.GetString(dataBytes);

    JsonUtility.FromJsonOverwrite(data, fileInfo.m_FileData);
  }

  // Will convert the level data to a Dictionary of elements up to the passed in version
  // If no version is passed in, we will flatten to the latest version
  Dictionary<Vector2Int, TileGrid.Element> GetGridDictionaryFromFileData(FileData fileData, Version? tempVersion = null)
  {
    // Sets the default value if no version is specified
    Version version = tempVersion ?? new(int.MaxValue, 0);

    Dictionary<Vector2Int, TileGrid.Element> tiles = new();

    // Load the version up the the specified manual save
    foreach (var levelData in fileData.m_ManualSaves)
    {
      // Stop flattening the level once we pass the version we want
      if (levelData.m_Version.m_ManualVersion > version.m_ManualVersion)
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

    // If we are loading a autosave, load the branch now
    if (!version.IsManual())
    {
      try
      {
        // Find the level data from the auto save version
        GetVersionLevelData(fileData, version, out LevelData autoSaveData);

        foreach (var tile in autoSaveData.m_AddedTiles)
        {
          tiles[tile.m_GridIndex] = tile;
        }
        foreach (var pos in autoSaveData.m_RemovedTiles)
        {
          tiles.Remove(pos);
        }
      }
      catch (InvalidOperationException)
      {
        Debug.Log($"Couldn't find {version} in file `{m_MountedFileInfo.m_SaveFilePath}");
        m_MainThreadDispatcher.Enqueue(() => StatusBar.Print("Error, couldn't find the proper autosave to load. Loaded branched manual instead."));
        // Just return the tiles we've loaded so far (the manual save)
      }
    }

    return tiles;
  }

  /// <summary>
  /// Splits a byte array at the first newline character.
  /// </summary>
  /// <param name="data">The data to split.</param>
  /// <param name="left">The left part of the split (before the newline).</param>
  /// <param name="right">The right part of the split (after the newline).</param>
  /// <exception cref="FormatException">Thrown when no newline character is found.</exception>
  void SplitNewLineBytes(in byte[] data, out byte[] left, out byte[] right)
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
    {
      throw new FormatException("No newline character found in the data");
    }
  }

  /// <summary>
  /// Removes a number od saved versions and saves the file
  /// </summary>
  /// <param name="fileInfo">The file info containing the save.</param>
  /// <param name="versions">A list of versions to delete.</param>
  /// <exception cref="Exception">Thrown when an error occurs.</exception>
  public void DeleteVersions(FileInfo fileInfo, List<Version> versions)
  {
    foreach (var version in versions)
    {
      DeleteVersionEx(fileInfo, version, false);
    }

    try
    {
      // If deleting from our own loaded file
      // Update the mounted data to the new data
      if (m_MountedFileInfo.m_SaveFilePath == fileInfo.m_SaveFilePath)
      {
        m_MountedFileInfo = fileInfo;
      }

      WriteDataToFile(fileInfo.m_SaveFilePath, fileInfo);
    }
    catch (Exception e)
    {
      throw new Exception($"Failed to save file after deleting multiple versions\nException {e.Message}, {e.GetType()}");
    }

    MoveFileItemToTop(m_SaveList, fileInfo.m_SaveFilePath);

    StatusBar.Print($"Sucessfuly deleted multiple versions from {fileInfo.m_SaveFilePath}");
  }

  /// <summary>
  /// Removes one saved version and saves the file
  /// </summary>
  /// <param name="fileInfo">The file info containing the save.</param>
  /// <param name="version">The version of the save to delete.</param>
  /// <exception cref="Exception">Thrown when an error occurs.</exception>
  /// 
  public void DeleteVersion(FileInfo fileInfo, Version version)
  {
    DeleteVersionEx(fileInfo, version, true);
  }

  private void DeleteVersionEx(FileInfo fileInfo, Version version, bool shouldSaveFile = true)
  {
    if (!FileDataExists(fileInfo.m_FileData))
      throw new Exception("No file data exists to delete version");

    if (version.IsManual())
    {
      // Loop to find our manual save
      for (int i = 0; i < fileInfo.m_FileData.m_ManualSaves.Count; ++i)
      {
        if (fileInfo.m_FileData.m_ManualSaves[i].m_Version != version)
          continue;

        // If this is the first manaul on the list, ie: no newer manual exists
        // We don't need to combine versions and can just delete this version
        if (i == fileInfo.m_FileData.m_ManualSaves.Count - 1)
        {
          fileInfo.m_FileData.m_ManualSaves.RemoveAt(i);
        }
        else
        {
          // Combine deltas and overwrite the newer version with the flattened data
          fileInfo.m_FileData.m_ManualSaves[i + 1] = FlattenLevelData(fileInfo.m_FileData.m_ManualSaves[i + 1], fileInfo.m_FileData.m_ManualSaves[i]);
          fileInfo.m_FileData.m_ManualSaves.RemoveAt(i);
        }

        DeleteBranchedAutoSaves(fileInfo, version.m_ManualVersion);

        if (shouldSaveFile)
          SaveAfterDeletion(fileInfo, version);

        UpdatedLoadedVersionIfDeleted(fileInfo, version);
        return;
      }
    }
    else
    {
      // Find auto save version
      for (int i = 0; i < fileInfo.m_FileData.m_AutoSaves.Count; ++i)
      {
        if (fileInfo.m_FileData.m_AutoSaves[i].m_Version == version)
        {
          fileInfo.m_FileData.m_AutoSaves.RemoveAt(i);

          if (shouldSaveFile)
            SaveAfterDeletion(fileInfo, version);

          UpdatedLoadedVersionIfDeleted(fileInfo, version);
          return;
        }
      }
    }

    throw new Exception($"Couldn't find {version} to delete");
  }

  private void UpdatedLoadedVersionIfDeleted(FileInfo fileInfo, Version version)
  {
    // If deleting from our own loaded file
    if (m_MountedFileInfo.m_SaveFilePath == fileInfo.m_SaveFilePath)
    {
      // If we deleted the version we had loaded, add on to the newest manaul save
      if (m_loadedVersion.m_ManualVersion == version.m_ManualVersion && fileInfo.m_FileData.m_ManualSaves.Count > 0)
      {
        m_loadedVersion = fileInfo.m_FileData.m_ManualSaves[^1].m_Version;
      }
    }
  }

  // Deletes all autosave off a versions branch
  private void DeleteBranchedAutoSaves(FileInfo fileInfo, int version)
  {
    if (!FileDataExists(fileInfo.m_FileData))
      throw new Exception("No file data exists to delete version");

    for (int i = 0; i < fileInfo.m_FileData.m_AutoSaves.Count; ++i)
    {
      if (fileInfo.m_FileData.m_AutoSaves[i].m_Version.m_ManualVersion == version)
      {
        fileInfo.m_FileData.m_AutoSaves.RemoveAt(i);
        --i;
      }
    }
  }

  private void SaveAfterDeletion(FileInfo fileInfo, Version version)
  {
    try
    {
      // If deleting from our own loaded file
      // Update the mounted data to the new data
      if (m_MountedFileInfo.m_SaveFilePath == fileInfo.m_SaveFilePath)
      {
        m_MountedFileInfo = fileInfo;
      }

      WriteDataToFile(fileInfo.m_SaveFilePath, fileInfo);
    }
    catch (Exception e)
    {
      throw new Exception($"Failed to save file after deleting {version}\nException {e.Message}, {e.GetType()}");

    }

    MoveFileItemToTop(m_SaveList, fileInfo.m_SaveFilePath);

    StatusBar.Print($"Sucessfuly deleted {version} from {fileInfo.m_SaveFilePath}");
  }

  // Combines two versions level data
  // Add the level data from "from" to "to"
  // Returns the combine data
  private LevelData FlattenLevelData(LevelData to, LevelData from)
  {
    Dictionary<Vector2Int, TileGrid.Element> squashedLevelAdd = new();
    HashSet<Vector2Int> squashedLevelRemove = new();

    SquashLevelDataAdder(ref squashedLevelAdd, ref squashedLevelRemove, from);
    SquashLevelDataAdder(ref squashedLevelAdd, ref squashedLevelRemove, to);

    to.m_AddedTiles = squashedLevelAdd.Values.ToList();
    to.m_RemovedTiles = squashedLevelRemove.ToList();

    return to;
  }

  // Adds the level datas deltas to an add/removed tiles arrays
  private void SquashLevelDataAdder(ref Dictionary<Vector2Int, TileGrid.Element> squashedLevelAdd, ref HashSet<Vector2Int> squashedLevelRemove, LevelData addedData)
  {
    foreach (var tile in addedData.m_AddedTiles)
    {
      // Add the tile to the list
      // If we had record to remove it earlier, remove the record
      if (squashedLevelRemove.Contains(tile.m_GridIndex))
        squashedLevelRemove.Remove(tile.m_GridIndex);
      squashedLevelAdd[tile.m_GridIndex] = tile;
    }
    foreach (var pos in addedData.m_RemovedTiles)
    {
      // Remove a tile if we have one
      if (squashedLevelAdd.ContainsKey(pos))
        squashedLevelAdd.Remove(pos);
      // Keep the remove in the list even if we deleted a tile, because the tile could be replacing a previously placed tile and we need to delete that too
      squashedLevelRemove.Add(pos);
    }
  }

  public void GetVersionLevelData(FileData fileData, Version version, out LevelData levelData)
  {
    if (fileData == null)
      throw new InvalidOperationException("File data is null");

    levelData = new();

    List<LevelData> levelList = version.IsManual() ? fileData.m_ManualSaves : fileData.m_AutoSaves;
    foreach (var data in levelList)
    {
      if (data.m_Version == version)
      {
        levelData = data;
        return;
      }
    }

    throw new InvalidOperationException($"{version} can not found");
  }

  // Finds the newest autosave from a manual save version
  // Returns 0 if no versions were found
  int GetLastAutoSaveVersion(FileData fileData, int manualVersion)
  {
    int lastVersion = 0;
    foreach (var data in fileData.m_AutoSaves)
    {
      if (data.m_Version.m_ManualVersion == manualVersion && data.m_Version.m_AutoVersion > lastVersion)
        lastVersion = data.m_Version.m_AutoVersion;
    }
    return lastVersion;
  }

  // Finds the newest autosave from a manual save version
  // Returns 0 if no versions were found
  int GetLastManualSaveVersion(FileData fileData)
  {
    int lastVersion = 0;
    foreach (var data in fileData.m_ManualSaves)
    {
      if (data.m_Version.m_ManualVersion > lastVersion)
        lastVersion = data.m_Version.m_ManualVersion;
    }
    return lastVersion;
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
          AddFileItemsForFiles(validFilePaths);
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

  void MoveFileItemToTop(UiListView fileList, string fullPath)
  {
    var item = fileList.GetItemByFullPath(fullPath);
    fileList.MoveToTop(item.transform);
  }

  void RemoveFileItem(UiListView fileList, string fullPath)
  {
    var element = fileList.GetItemByFullPath(fullPath);
    fileList.Remove(element.GetComponent<RectTransform>());
  }

  void AddFileItemForFile(UiListView fileList, string fullPath)
  {
    var fileName = Path.GetFileNameWithoutExtension(fullPath);
    var rt = AddHelper(fullPath, fileName);
    fileList.Add(rt);
  }

  void AddFileItemsForFiles(string[] fullPaths)
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
    var listItem = Instantiate(m_FileItemPrefab);

    listItem.Setup(fullPath, fileName, File.GetLastWriteTime(fullPath).ToString("g"));
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

  /// <summary>
  /// Creates a temporary file name with a random GUID.
  /// </summary>
  /// <returns>The full path to the temporary file</returns>
  string CreateTempFileName()
  {
    return Path.Combine(m_CurrentDirectoryPath, Guid.NewGuid().ToString() + s_FilenameExtension);
  }

  /// <summary>
  /// Sorts an array of file paths by their last modified date.
  /// </summary>
  /// <param name="files">The array of file paths to sort</param>
  void SortByDateModified(string[] files)
  {
    Array.Sort(files, DateModifiedComparison);
  }

  /// <summary>
  /// Compares two file paths by their last modified date.
  /// </summary>
  /// <param name="a">The first file path</param>
  /// <param name="b">The second file path</param>
  /// <returns>A comparison value indicating the relative order of the files</returns>
  static int DateModifiedComparison(string a, string b)
  {
    var dateTimeA = File.GetLastWriteTime(a);
    var dateTimeB = File.GetLastWriteTime(b);

    return DateTime.Compare(dateTimeA, dateTimeB);
  }

  /// <summary>
  /// Utility class for compressing and decompressing string data using GZip.
  /// </summary>
  protected class StringCompression
  {
    /// <summary>
    /// Compresses the input string data using GZip.
    /// </summary>
    /// <param name="input">The string to compress</param>
    /// <returns>The compressed data as a byte array</returns>
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

    /// <summary>
    /// Decompresses the input data using GZip.
    /// </summary>
    /// <param name="compressedData">The compressed data as a byte array</param>
    /// <returns>The decompressed string</returns>
    public static string Decompress(byte[] compressedData)
    {
      using MemoryStream ms = new(compressedData);
      using GZipStream sr = new(ms, CompressionMode.Decompress);
      using StreamReader reader = new(sr);
      return reader.ReadToEnd();
    }
  }

  /// <summary>
  /// A thread-safe dispatcher for executing actions on the main Unity thread.
  /// Used by background threads to queue up actions that must run on the main thread.
  /// </summary>
  public class MainThreadDispatcher
  {
    private static readonly object s_LockObject = new();
    private Queue<System.Action> m_ActionQueue = new();

    /// <summary>
    /// Runs all the queued actions in the list.
    /// This should only be called from the main Unity thread.
    /// </summary>
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

    /// <summary>
    /// Adds a function call to the queue to be executed on the main thread.
    /// This can be called from any thread.
    /// </summary>
    /// <param name="action">The action to execute on the main thread</param>
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

// TODO, Star on file name to show unsaved changes.

// Extra credit
// TODO, fix area placement taking forever
// TODO, Large level creation and deletion still takes a long time.


// These three are the same:
// TODO, game exit or file load "Are you sure" when there are unsaved changes or no file is mounted
// TODO, add are you sure, if you load a level with unsaved changes.
// TODO, When closeing app, ask to save if there are unsaved changes.
// If no, delete temp file on close
// OOOR we could just autosave to the latest manual


// __Needs UI__
// TONOTDO, add feature to select a range of versions and squash them together
//   No, just have them delete the old ones instead. It would do the same thing
// TODO, right click version to delete auto or manual save
// Unsaved changes prompt
// Warning about max manual saves reached


// If they want an auto save to be perminant, then load it and save.
// Should we have a "delete all auto saves" button?

// TODO: If the last version is deleted from a save, show pop up asking "If you delete this (or theses) save(s) the file will be deleted. Do you wish to continue?"
// then delete the file, unmount, and close the info window.
// Either that, or just create a new version with nothing in it.

// TODO: Be able to delete a file from the save files bar.

// TODO: Discuss, should we allow saving empty files. Ie save a level with no blocks?
