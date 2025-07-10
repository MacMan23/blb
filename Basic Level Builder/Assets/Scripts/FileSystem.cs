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
using static FileVersioning;

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
  public UiSaveFileItem m_FileItemPrefab;
  public UiListView m_SaveList;
  public TileGrid m_TileGrid;

  UnityDragAndDropHook m_DragAndDropHook;

  ModalDialogMaster m_ModalDialogMaster;
  [SerializeField]
  protected ModalDialogAdder m_OverrideDialogAdder;
  [SerializeField]
  protected ModalDialogAdder m_SaveAsDialogAdder;
  [SerializeField]
  protected ModalDialogAdder m_ExportAsDialogAdder;

  protected string m_CurrentDirectoryPath;
  protected string m_PendingSaveFullFilePath = "";
  protected FileData m_PendingExportFileData = null;
  protected List<FileVersion> m_PendingExportVersions = null;

  FileInfo m_MountedFileInfo;

  // The version of the manual or autosave that is loaded
  FileVersion m_loadedVersion;

  // A thread to run when saving should be performed.
  // Only one save thread is run at once.
  private Thread m_SavingThread;
  // A queue of events that the saving thread will enqueue for the main thread
  protected readonly MainThreadDispatcher m_MainThreadDispatcher = new();

  [DllImport("__Internal")]
  private static extern void SyncFiles();

  #region FileStructure classes

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

  public struct FileInfo
  {
    public bool m_IsTempFile;
    public string m_SaveFilePath;
    public FileData m_FileData;
    public FileHeader m_FileHeader;
  }

  [Serializable]
  public class FileHeader
  {
    public FileHeader(string ver = "", bool shouldCompress = false)
    {
      m_BlbVersion = ver;
      m_IsDataCompressed = shouldCompress;
    }
    public string m_BlbVersion;
    public bool m_IsDataCompressed = false;
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
  public class LevelData
  {
    public LevelData()
    {
      m_AddedTiles = new List<TileGrid.Element>();
      m_RemovedTiles = new List<Vector2Int>();
    }

    public FileVersion m_Version;
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
        Save(false, tempPath, false);

        StatusBar.Print(errorString);
      }

      // Update file list incase files were added or removed
      SetDirectoryName(m_DefaultDirectoryName);
    }
  }

  /// <summary>
  /// Creates new file data structures.
  /// </summary>
  private static void CreateFileInfo(out FileInfo fileInfo, string filePath = "")
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
  private void ClearFileData(FileInfo fileInfo)
  {
    fileInfo.m_FileHeader = null;
    fileInfo.m_FileData = null;
  }

  /// <summary>
  /// Checks if file data exists.
  /// </summary>
  /// <returns>True if file data exists, false otherwise</returns>
  public static bool FileDataExists(FileData fileData)
  {
    return fileData != null;
  }

  /// <summary>
  /// Unmounts the current file.
  /// </summary>
  private void UnmountFile()
  {
    m_MountedFileInfo.m_SaveFilePath = "";
  }

  /// <summary>
  /// Mounts a file at the specified path.
  /// </summary>
  /// <param name="filePath">The path to the file to mount</param>
  private void MountFile(string filePath, FileInfo fileInfo)
  {
    m_MountedFileInfo = fileInfo;
    m_MountedFileInfo.m_SaveFilePath = filePath;
  }

  private bool FileExists(string filePath)
  {
    return !String.IsNullOrEmpty(filePath) && File.Exists(filePath);
  }

  private bool IsFileMounted()
  {
    return !String.IsNullOrEmpty(m_MountedFileInfo.m_SaveFilePath);
  }

  private void OnDroppedFiles(List<string> paths, POINT dropPoint)
  {
    if (m_ModalDialogMaster.m_Active || GlobalData.AreEffectsUnderway() || GlobalData.IsInPlayMode())
      return;

    var validPaths = paths.Where(path => path.EndsWith(".blb")).ToList();

    if (validPaths.Count == 0)
      StatusBar.Print("Drag and drop only supports <b>.blb</b> files.");
    else
      LoadFromFullFilePathEx(validPaths[0]);
  }

  protected void Save(bool autosave, string saveAsFileName = null, bool shouldPrintElapsedTime = true)
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
        m_PendingSaveFullFilePath = destFilePath;

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

  protected void StartSavingThread(string destFilePath, bool autosave, bool isSaveAs = false, bool shouldPrintElapsedTime = true)
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

  protected void StartExportSavingThread(string destFilePath)
  {
    m_SavingThread = new Thread(new ParameterizedThreadStart(ExportSavingThread));

    m_SavingThread.Start(destFilePath);
  }

  private void SavingThreadFlatten(object threadParameters)
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

    m_MountedFileInfo.m_FileData.m_ManualSaves.Add(levelData);

    try
    {
      bool shouldMountSave = true;
      bool isAutosave = false;
      bool shouldCopyFile = false;
      bool shouldPrintElapsedTime = (bool)parameters[2];
      WriteDataToFile(destFilePath, m_MountedFileInfo, shouldMountSave, isOverwriting, startTime, isAutosave, shouldCopyFile, shouldPrintElapsedTime);
      m_loadedVersion = levelData.m_Version;
    }
    catch (Exception e)
    {
      var errorString = $"Error while flattening and saving file: {e.Message} ({e.GetType()})";
      m_MainThreadDispatcher.Enqueue(() => StatusBar.Print(errorString));
      Debug.LogError(errorString);
    }
  }

  private void SavingThread(object threadParameters)
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
    // #: Overwriting, MountedFile, Differences, Saving to mounted file
    // 1: 1, 0, 0, 0 (Save as; we are writing to an existing file, yet we have no mounted file. Thus we just save our editor level) [TileGrid]
    // 2: 1, 1, 0, 0 (Overwrite save to our mounted file or another file. No changes, so just copy our file over) [File copy]
    // 3: 1, 1, 1, 0 (Overwrite save to our mounted file or another file. Add changes to mounted file string) [oldSave + diff]
    // 4: 0, 1, 0, 0 (Save as; Copy our mounted file to a new file) [File copy]
    // 5: 0, 1, 1, 0 (Save as; Copy our level with the differences added to a new file) [oldSave + diff]
    // 6: 0, 0, 0, 0 (Save as; Write editor level to file) [TileGrid]
    // 7: 1, 1, 0, 1 (Skip, We are saving to our own file, yet we have no differences) [return]
    // 8: 1, 1, 1, 1 (Save to our file with the differences) [oldSave + diff]
    // We can't have differences if we don't have a mounted file
    // We can only save to the mounted file if the file exist, meaning overwriting is true.
    // We can't save to the mounted file if we have no mounted file

    // If we will be copying the mounted file over to a diffrent file
    bool copyFile = false;
    bool hasDifferences = GetDifferences(out LevelData levelData, m_MountedFileInfo, m_TileGrid);

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

    // TODO, check if the auto save has differences from the last auto save, if not, discard save,
    // TODO, check if manual save is the same as the last auto save, if so just move auto to manual
    // Only write if we have differences, or we have no user created file yet
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

        m_MountedFileInfo.m_FileData.m_ManualSaves.Add(levelData);
      }
      // If this is an auto save, store what version of the manual save we branched from to get these differences to save
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

  private void ExportSavingThread(object threadParameter)
  {
    var startTime = DateTime.Now;

    // Extract the parameters from the object array
    string destFilePath = (string)threadParameter;

    bool isOverwriting = File.Exists(destFilePath);

    CreateFileInfo(out FileInfo sourceInfo, destFilePath);

    // If we are exporting out multiple versions, this variable should exist
    if (m_PendingExportVersions != null)
      ExtractSelectedVersions(ref m_PendingExportFileData, m_PendingExportVersions);

    sourceInfo.m_FileData = m_PendingExportFileData;

    // Null out data so we know if we finished our export
    m_PendingExportFileData = null;
    m_PendingExportVersions = null;

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

  private void UpdateFileToItemList(string fullFilePath, bool overwriting)
  {
    if (overwriting)
      m_MainThreadDispatcher.Enqueue(() => MoveFileItemToTop(m_SaveList, fullFilePath));
    else
      m_MainThreadDispatcher.Enqueue(() => AddFileItemForFile(m_SaveList, fullFilePath));
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
  protected void WriteDataToFile(string destFilePath, FileInfo sourceFileInfo, bool shouldMountSave,
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
  protected void WriteDataToFile(string destFilePath, FileInfo sourceFileInfo)
  {
    List<byte> data = new();
    data.AddRange(System.Text.Encoding.Default.GetBytes(JsonUtility.ToJson(sourceFileInfo.m_FileHeader) + "\n"));
    if (s_ShouldCompress)
      data.AddRange(StringCompression.Compress(JsonUtility.ToJson(sourceFileInfo.m_FileData)));
    else
      data.AddRange(System.Text.Encoding.Default.GetBytes(JsonUtility.ToJson(sourceFileInfo.m_FileData)));

    File.WriteAllBytes(destFilePath, data.ToArray());
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
  /// Gets the file info from a file at the specified path.
  /// </summary>
  /// <param name="fullFilePath">The full path to the file.</param>
  /// <param name="fileInfo">The file info to populate.</param>
  /// <exception cref="Exception">Thrown when the file cannot be found.</exception>
  protected void GetFileInfoFromFullFilePathEx(string fullFilePath, out FileInfo fileInfo)
  {
    CreateFileInfo(out fileInfo, fullFilePath);
    if (!File.Exists(fullFilePath))
    {
      throw new Exception($"File not found: {fullFilePath}");
    }

    GetDataFromJson(File.ReadAllBytes(fullFilePath), fileInfo);
  }

  protected void LoadFromFullFilePathEx(string fullFilePath, FileVersion? version = null)
  {
    if (GlobalData.AreEffectsUnderway())
      return;

    try
    {
      if (!FileDataExists(m_MountedFileInfo.m_FileData))
        CreateFileInfo(out m_MountedFileInfo);

      MountFile(fullFilePath, m_MountedFileInfo);
      LoadFromJson(File.ReadAllBytes(fullFilePath), version);

      m_loadedVersion = version ?? new(GetLastManualSaveVersion(m_MountedFileInfo.m_FileData), 0);
    }
    catch (Exception e)
    {
      // File not loaded, remove file mount
      UnmountFile();
      Debug.LogError($"Error while loading. {e.Message} ({e.GetType()})");
    }
  }

  protected void LoadFromTextAssetEx(TextAsset level)
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
  private void LoadFromJson(byte[] json, FileVersion? version = null)
  {
    // Make sure we have file data for the load
    if (!FileDataExists(m_MountedFileInfo.m_FileData))
      CreateFileInfo(out m_MountedFileInfo);

    GetDataFromJson(json, m_MountedFileInfo);

    m_TileGrid.LoadFromDictonary(GetGridDictionaryFromFileData(m_MountedFileInfo, version));
  }

  /// <summary>
  /// Grabs data from file and stores it in passed in FileData and a Header.
  /// </summary>
  /// <param name="json">The JSON data as bytes.</param>
  /// <param name="fileInfo">The file info to populate.</param>
  /// <exception cref="FormatException">Thrown when the file format is invalid.</exception>
  private void GetDataFromJson(byte[] json, FileInfo fileInfo)
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

  /// <summary>
  /// Splits a byte array at the first newline character.
  /// </summary>
  /// <param name="data">The data to split.</param>
  /// <param name="left">The left part of the split (before the newline).</param>
  /// <param name="right">The right part of the split (after the newline).</param>
  /// <exception cref="FormatException">Thrown when no newline character is found.</exception>
  private void SplitNewLineBytes(in byte[] data, out byte[] left, out byte[] right)
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

  protected void UpdateLoadedVersionIfDeleted(FileInfo fileInfo, FileVersion version)
  {
    // If deleting from our own loaded file
    if (m_MountedFileInfo.m_SaveFilePath == fileInfo.m_SaveFilePath)
    {
      // Mark the version we have loaded from to be the newest one
      if (m_loadedVersion.m_ManualVersion == version.m_ManualVersion && fileInfo.m_FileData.m_ManualSaves.Count > 0)
      {
        m_loadedVersion = fileInfo.m_FileData.m_ManualSaves[^1].m_Version;
      }
    }
  }

  protected void SaveAfterDeletion(FileInfo fileInfo, string versionDescription)
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
      throw new Exception($"Failed to save file after deleting {versionDescription}\nException {e.Message}, {e.GetType()}");

    }

    MoveFileItemToTop(m_SaveList, fileInfo.m_SaveFilePath);

    StatusBar.Print($"Sucessfuly deleted {versionDescription} from {fileInfo.m_SaveFilePath}");
  }

  private void SetDirectoryName(string name)
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

  private void MoveFileItemToTop(UiListView fileList, string fullFilePath)
  {
    var item = fileList.GetItemByFullFilePath(fullFilePath);
    fileList.MoveToTop(item.transform);
  }

  private void RemoveFileItem(UiListView fileList, string fullFilePath)
  {
    var element = fileList.GetItemByFullFilePath(fullFilePath);
    fileList.Remove(element.GetComponent<RectTransform>());
  }

  private void AddFileItemForFile(UiListView fileList, string fullFilePath)
  {
    var fileName = Path.GetFileNameWithoutExtension(fullFilePath);
    var rt = AddHelper(fullFilePath, fileName);
    fileList.Add(rt);
  }

  private void AddFileItemsForFiles(string[] fullFilePaths)
  {
    if (GlobalData.AreEffectsUnderway())
      return;

    var listItems = new List<RectTransform>();

    foreach (var fullFilePath in fullFilePaths)
    {
      // Path.GetFileNameWithoutExtension can only throw ArgumentException
      // for the path having invalid characters, and AddHelper will only be
      // called after ValidateDirectoryName has cleared the path
      var fileName = Path.GetFileNameWithoutExtension(fullFilePath);

      var rt = AddHelper(fullFilePath, fileName);
      listItems.Add(rt);
    }

    m_SaveList.Add(listItems);
  }

  private RectTransform AddHelper(string fullFilePath, string fileName)
  {
    var listItem = Instantiate(m_FileItemPrefab);

    listItem.Setup(fullFilePath, fileName, File.GetLastWriteTime(fullFilePath).ToString("g"));
    var rt = listItem.GetComponent<RectTransform>();

    return rt;
  }

  private bool ValidateDirectoryName(string directoryName)
  {
    if (string.IsNullOrEmpty(directoryName) || string.IsNullOrWhiteSpace(directoryName))
      return false;

    var invalidChars = Path.GetInvalidPathChars();

    if (invalidChars.Length > 0)
      return directoryName.IndexOfAny(invalidChars) < 0;
    else
      return true;
  }

  private string GetDocumentsPath()
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
  private string CreateTempFileName()
  {
    return Path.Combine(m_CurrentDirectoryPath, Guid.NewGuid().ToString() + s_FilenameExtension);
  }

  /// <summary>
  /// Sorts an array of file paths by their last modified date.
  /// </summary>
  /// <param name="files">The array of file paths to sort</param>
  private void SortByDateModified(string[] files)
  {
    Array.Sort(files, DateModifiedComparison);
  }

  /// <summary>
  /// Compares two file paths by their last modified date.
  /// </summary>
  /// <param name="a">The first file path</param>
  /// <param name="b">The second file path</param>
  /// <returns>A comparison value indicating the relative order of the files</returns>
  private static int DateModifiedComparison(string a, string b)
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
// TONOTDO, add feature to select a range of versions and Flatten them together
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
