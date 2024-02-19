using System;
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
  readonly static public int s_MaxAutosaveCount = 20;
  readonly static bool s_ShouldCompress = true;
  static string s_EditorVersion;

  public string m_DefaultDirectoryName = "Default Project";
  public UiHistoryItem m_HistoryItemPrefab;
  public UiListView m_SaveList;
  public TileGrid m_TileGrid;

  UnityDragAndDropHook m_DragAndDropHook;

  ModalDialogMaster m_ModalDialogMaster;
  ModalDialogAdder m_DialogAdder;
  string m_CurrentDirectoryPath;
  string m_PendingSaveFullPath = "";

  string m_MountedSaveFilePath = "";
  FileData m_MountedFileData;
  Header m_MountedFileHeader;

  // A thread to run when saving should be performed.
  // Only one save thread is run at once.
  private Thread m_SavingThread;
  // A queue of events that the saving thread will enqueue for the main thread
  private MainThreadDispatcher m_MainThreadDispatcher = new();

  [DllImport("__Internal")]
  private static extern void SyncFiles();

  #region FileStructure classes
  [Serializable]
  class FileData
  {
    public FileData()
    {
      manualSaves = new List<LevelData>();
      autoSaves = new List<LevelData>();
    }
    public List<LevelData> manualSaves;
    public List<LevelData> autoSaves;
  }

  [Serializable]
  class Header
  {
    public Header(string ver = "")
    {
      blbVersion = ver;
    }
    public string blbVersion;
    public bool isDataCompressed = false;
  }

  [Serializable]
  public class LevelData
  {
    public LevelData()
    {
      addedTiles = new List<TileGrid.Element>();
      removedTiles = new List<Vector2Int>();
    }

    public int version;
    // The manual save version the auto save branched off from
    public int branchVersion;
    public DateTime timeStamp;
    public List<TileGrid.Element> addedTiles;
    public List<Vector2Int> removedTiles;
  }
  #endregion

  // Start is called before the first frame update
  void Start()
  {
    s_EditorVersion = Application.version;
    m_ModalDialogMaster = FindObjectOfType<ModalDialogMaster>();

    m_DialogAdder = GetComponent<ModalDialogAdder>();
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
    m_MountedFileHeader = new(s_EditorVersion);
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

        m_DialogAdder.RequestDialogAtCenterWithStrings(0, Path.GetFileName(fullPath));
        return;
      }
    }
    else
    {
      if (IsFileMounted())
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
      else
      {
        // We are doing a manual save with no file mounted
        // Or auto save with no loaded file
        // If we are doing a manual save,
        // request a name for the new file to save to
        // Skip over the save if an auto save since it would prompt a file name
        if (!autosave)
          m_DialogAdder.RequestDialogAtCenterWithStrings(1);
        return;
      }
    }

    // TODO, add autos to save file
    WriteHelper(fullPath, autosave);
  }


  public void ConfirmOverwrite()
  {
    WriteHelper(m_PendingSaveFullPath, false);
  }

  void WriteHelper(string fullPath, bool autosave)
  {
    // Copy the map data into a buffer to use for the saving thread.
    m_TileGrid.CopyGridBuffer();

    // Define parameters for the branched thread function
    object[] parameters = { fullPath, autosave };

    // Create a new thread and pass the ParameterizedThreadStart delegate
    m_SavingThread = new Thread(new ParameterizedThreadStart(WriteHelperThread));

    m_SavingThread.Start(parameters);
  }

  void WriteHelperThread(object threadParameters)
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
    // 7: 1, 1, 0, 1 (Skip, We are saving to our own file, yeSt we have no diffrences) [return]
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
    if (overwriting && IsFileMounted() && fullPath.Equals(m_MountedSaveFilePath) && !hasDifferences)
    {
      // #7
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
      if (s_MaxAutosaveCount <= 0)
        return;

      // now, if the autosave count is at its limit, then we should
      // get rid of the oldest autosave
      if (IsFileMounted() && m_MountedFileData.autoSaves.Count >= s_MaxAutosaveCount)
      {
        m_MountedFileData.autoSaves.RemoveAt(0);
      }
    }

    if (hasDifferences)
    {
      // #6, 1, 3, 5, 8
      // We have data to add/overwite to any file

      m_MountedFileHeader.isDataCompressed = s_ShouldCompress;

      levelData.timeStamp = DateTime.Now;

      // Set the version of the save based off the last save
      List<LevelData> savesList = autosave ? m_MountedFileData.autoSaves : m_MountedFileData.manualSaves;
      if (savesList.Count > 0)
      {
        levelData.version = savesList[^1].version + 1;
      }
      else
      {
        levelData.version = 1;
      }

      // If this is an auto save, store what version of the manual save we branched from to get these diffrences to save
      if (autosave)
      {
        // If an auto save happens and no file is mounted, a manual save is prompted.
        levelData.branchVersion = m_MountedFileData.manualSaves[^1].version;
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

    try
    {
      if (copyFile)
      {
        File.Copy(m_MountedSaveFilePath, fullPath, true);
      }
      else
      {
        string data = JsonUtility.ToJson(m_MountedFileHeader) + Environment.NewLine;
        if (s_ShouldCompress)
          data += StringCompression.Compress(JsonUtility.ToJson(m_MountedFileData));
        else
          data += JsonUtility.ToJson(m_MountedFileData);

        File.WriteAllText(fullPath, data);
      }

      // Mount the file now that everything has been written
      MountFile(fullPath);

      if (overwriting)
        m_MainThreadDispatcher.Enqueue(() => MoveHistoryItemToTop(m_SaveList, fullPath));
      else
        m_MainThreadDispatcher.Enqueue(() => AddHistoryItemForFile(m_SaveList, fullPath));

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
    if (s_ShouldCompress)
      jsonString = StringCompression.Compress(jsonString);

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
      LoadFromJson(text);
    }
  }


  public void LoadFromFullPath(string fullPath)
  {
    if (GlobalData.AreEffectsUnderway())
      return;

    try
    {
      MountFile(fullPath);
      LoadFromJson(File.ReadAllText(fullPath));
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
    LoadFromJson(level.text);
  }

  // Intermidiatarty load function. Calls the rest of the load functions.
  void LoadFromJson(string json)
  {
    // Make sure we have file data for the load
    if (!FileDataExists())
      CreateFileData();

    try
    {
      // Read the header first
      // The header is always uncompressed, and the data might be
      string[] split = json.Split(Environment.NewLine, 2);

      if (split.Length != 2)
        throw new ArgumentException("Header and or Level data can not be found");

      JsonUtility.FromJsonOverwrite(split[0], m_MountedFileHeader);

      // If the save file was made with a diffrent version
      if (!m_MountedFileHeader.blbVersion.Equals(s_EditorVersion))
      {
        Debug.Log($"Save file {Path.GetFileName(m_MountedSaveFilePath)} was made with a diffrent BLB version. There may be possible errors.");
        m_MainThreadDispatcher.Enqueue(() =>
        StatusBar.Print($"Save file {Path.GetFileName(m_MountedSaveFilePath)} was made with a diffrent BLB version. There may be possible errors."));
        // TODO, should we return or keep going? Do we want to run a file with a diff version?
      }

      // Decompress string if needed
      if (m_MountedFileHeader.isDataCompressed)
        split[1] = StringCompression.Decompress(split[1]);

      JsonUtility.FromJsonOverwrite(split[1], m_MountedFileData);
    }
    catch (System.ArgumentException e)
    {
      Debug.Log($"Error loading save file {Path.GetFileName(m_MountedSaveFilePath)} : {e}");
      m_MainThreadDispatcher.Enqueue(() =>
      StatusBar.Print($"Error loading save file {Path.GetFileName(m_MountedSaveFilePath)} : {e}"));
      return;
    }

    m_TileGrid.LoadFromDictonary(FlattenLevelStringToGrid());
  }

  Dictionary<Vector2Int, TileGrid.Element> FlattenLevelStringToGrid()
  {
    Dictionary<Vector2Int, TileGrid.Element> tiles = new();

    foreach (var levelData in m_MountedFileData.manualSaves)
    {
      foreach (var tile in levelData.addedTiles)
      {
        tiles.Add(tile.m_GridIndex, tile);
      }
      foreach (var pos in levelData.removedTiles)
      {
        tiles.Remove(pos);
      }
    }

    return tiles;
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
    Destroy(element.gameObject);
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
    public static string Compress(string input)
    {
      byte[] byteArray = System.Text.Encoding.UTF8.GetBytes(input);

      using MemoryStream ms = new();
      using (GZipStream sw = new(ms, CompressionMode.Compress))
      {
        sw.Write(byteArray, 0, byteArray.Length);
      }
      return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    // Decompresses the input data using GZip
    public static string Decompress(string compressedData)
    {
      byte[] compressedBytes = System.Text.Encoding.UTF8.GetBytes(compressedData);

      using (MemoryStream ms = new(compressedBytes))
      using (GZipStream sr = new(ms, CompressionMode.Decompress))
      using (StreamReader reader = new(sr, System.Text.Encoding.UTF8))
      {
        return reader.ReadToEnd();
      }
    }
  }

  // A list of actions in a queue for use by threads to store unity internal related functions that must be run on the main thread.
  public class MainThreadDispatcher
  {
    private static readonly object s_LockObject = new object();
    private Queue<System.Action> m_ActionQueue = new Queue<System.Action>();

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

// TODO, add are you sure, if you load a level with unsaved changes.
// TODO, fix area placement taking forever
// TODO, Large level creation and deletion still takes a long time.
// TODO, game exit or file load "Are you sure" when there are unsaved changes or no file is mounted
// TODO, Work needs to be done to mark what the previous version the auto save was used to make the diffrences from.







