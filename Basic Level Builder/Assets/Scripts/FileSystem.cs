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
using System.Text;

public class FileSystem : MonoBehaviour {
  readonly static public string s_FilenameExtension = ".blb";
  readonly static public string s_RootDirectoryName = "Basic Level Builder";
  readonly static public string s_DateTimeFormat = "h-mm-ss.ff tt, ddd d MMM yyyy";
  readonly static string[] s_LineSeparator = new string[] { Environment.NewLine };
  readonly static bool s_ShouldCompress = true;

  public string m_DefaultDirectoryName = "Default Project";
  public UiHistoryItem m_HistoryItemPrefab;
  public UiListView m_ManualSaveList;
  public UiListView m_AutosaveList;
  public TileGrid m_TileGrid;
  public int m_MaxAutosaveCount = 100;

  UnityDragAndDropHook m_DragAndDropHook;

  ModalDialogMaster m_ModalDialogMaster;
  ModalDialogAdder m_OverwriteConfirmationDialogAdder;
  string m_CurrentDirectoryPath;
  int m_CurrentAutosaveCount = 0;
  string m_PendingSaveFullPath = "";

  string m_MountedSaveFilePath = "";
  int m_latestLevelVersion = 0;

  // A thread to run when saving should be performed.
  // Only one save thread is run at once.
  private Thread m_SavingThread;
  // A queue of events that the saving thread will enqueue for the main thread
  private MainThreadDispatcher m_MainThreadDispatcher = new();

  [DllImport("__Internal")]
  private static extern void SyncFiles();


  // Start is called before the first frame update
  void Start()
  {
    m_ModalDialogMaster = FindObjectOfType<ModalDialogMaster>();

    m_OverwriteConfirmationDialogAdder = GetComponent<ModalDialogAdder>();
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

    if (autosave)
    {
      // first of all, if m_MaxAutosaveCount <= 0, then no autosaving
      // should occur at all
      if (m_MaxAutosaveCount <= 0)
        return;

      // now, if the autosave count is at its limit, then we should
      // get rid of the oldest autosave
      if (m_CurrentAutosaveCount >= m_MaxAutosaveCount)
      {
        var oldestAutosave = m_AutosaveList.GetOldestItem();
        var itemToRemove = oldestAutosave.GetComponent<RectTransform>();
        var pathToRemove = oldestAutosave.m_FullPath;

        try
        {
          File.Delete(pathToRemove);

          m_AutosaveList.Remove(itemToRemove);
          Destroy(oldestAutosave.gameObject);
          --m_CurrentAutosaveCount;
        }
        catch (Exception e)
        {
          var errorString = $"Error while deleting old autosave. {e.Message} ({e.GetType()})";
          StatusBar.Print(errorString);
          Debug.LogError(errorString);
        }
      }
    }

    string fullPath;
    // If we are doing a SAVE AS
    if (name != null)
    {
      fullPath = Path.Combine(m_CurrentDirectoryPath, name + s_FilenameExtension);

      // Give prompt if we are going to write to and existing file
      if (File.Exists(fullPath))
      {
        m_PendingSaveFullPath = fullPath;

        m_OverwriteConfirmationDialogAdder.RequestDialogsAtCenterWithStrings(
          Path.GetFileName(fullPath));
        return;
      }
    }
    else
    {
      // If we have no mounted save file
      if (m_MountedSaveFilePath.Equals(""))
        // TODO, put the auto saves into the mounted file
        fullPath = Path.Combine(m_CurrentDirectoryPath, GenerateFileName(autosave));
      else
        fullPath = m_MountedSaveFilePath;
    }

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



    #region Add level changes to level data
    // TODO, Just copying the old level to a new file can be optimized
    string jsonString;

    string diffrences = m_TileGrid.GetDiffrences();
    string oldLevel = RawDataToJsonString(File.ReadAllBytes(fullPath));

    // Optimized code
    // Check if we are writing to a file where we are adding on to our old version with our new additions
    if ((overwriting || !m_MountedSaveFilePath.Equals("")) && !diffrences.Equals(""))
    {
      // Record changes as a new version
      jsonString = $"{oldLevel}\n@{++m_latestLevelVersion}\n{diffrences}";
    }
    else
    {
      // We have no new changes so, just copy the file
      if (!m_MountedSaveFilePath.Equals("") || overwriting)
      {
        jsonString = oldLevel;
      }
      // We have no history/mounted file
      else
      {
        jsonString = $"@{++m_latestLevelVersion}\n{m_TileGrid.ToJsonString()}";
      }
    }

    // Mount to this file
    m_MountedSaveFilePath = fullPath;
    #endregion

    try
    {
      byte[] data;
      if (s_ShouldCompress)
        data = StringCompression.Compress(jsonString);
      else
        data = System.Text.Encoding.UTF8.GetBytes(jsonString);

      File.WriteAllBytes(fullPath, data);

      var listToAddTo = autosave ? m_AutosaveList : m_ManualSaveList;

      if (overwriting)
        m_MainThreadDispatcher.Enqueue(() => MoveHistoryItemToTop(listToAddTo, fullPath));
      else
        m_MainThreadDispatcher.Enqueue(() => AddHistoryItemForFile(listToAddTo, fullPath));

      if (autosave)
        ++m_CurrentAutosaveCount;

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

  int GetLevelVersion(string fullFilePath)
  {
    return 0;
  }


  public void CopyToClipboard()
  {
    if (GlobalData.AreEffectsUnderway())
      return;

    var jsonString = m_TileGrid.ToJsonString();
    // If we are useing a compression alg for loading/saving, copy this level as a copressed string
    if (s_ShouldCompress)
      jsonString = System.Text.Encoding.UTF8.GetString(StringCompression.Compress(jsonString));

    var te = new TextEditor();
    te.text = jsonString;
    te.SelectAll();
    te.Copy();

    StatusBar.Print("Level copied to clipboard.");
  }


  public void LoadFromClipboard()
  {
    if (GlobalData.AreEffectsUnderway())
      return;

    var te = new TextEditor();
    te.multiline = true;
    te.Paste();
    var text = te.text;

    if (string.IsNullOrEmpty(text))
    {
      StatusBar.Print("You tried to paste a level from the clipboard, but it's empty.");
    }
    else
    {
      // There is no file loaded from, so mount no files
      m_MountedSaveFilePath = "";
      LoadFromJson(text);
    }
  }


  public void LoadFromFullPath(string fullPath)
  {
    if (GlobalData.AreEffectsUnderway())
      return;

    try
    {
      m_MountedSaveFilePath = fullPath;
      LoadFromJson(File.ReadAllBytes(fullPath));
    }
    catch (Exception e)
    {
      // File not loaded, remove file mount
      m_MountedSaveFilePath = "";
      Debug.LogError($"Error while loading. {e.Message} ({e.GetType()})");
    }
  }


  public void LoadFromTextAsset(TextAsset level)
  {
    if (GlobalData.AreEffectsUnderway())
      return;

    // There is no file loaded from, so mount no files
    m_MountedSaveFilePath = "";
    LoadFromJson(level.bytes);
  }

  string RawDataToJsonString(byte[] json)
  {
    if (s_ShouldCompress)
      return StringCompression.Decompress(json);
    else
      return System.Text.Encoding.UTF8.GetString(json);
  }

  // Overloaded function to convert a string to a byte array for the main function.
  void LoadFromJson(string json)
  {
    LoadFromJson(System.Text.Encoding.UTF8.GetBytes(json));
  }
  // Intermidiatarty load function. Calls the rest of the load functions.
  void LoadFromJson(byte[] json)
  {
    LoadFromJsonStrings(JsonStringsFromSingleString(RawDataToJsonString(json)));
  }


  // Splits the json lines into an array
  protected string[] JsonStringsFromSingleString(string singleString)
  {
    return singleString.Split(s_LineSeparator, StringSplitOptions.RemoveEmptyEntries);
  }


  void LoadFromJsonStrings(string[] jsonStrings)
  {
    m_TileGrid.LoadFromJsonStrings(jsonStrings);
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
          m_ManualSaveList.Clear();
          m_AutosaveList.Clear();
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


  void AddHistoryItemForFile(UiListView historyList, string fullPath)
  {
    if (GlobalData.AreEffectsUnderway())
      return;

    var fileName = Path.GetFileNameWithoutExtension(fullPath);
    var rt = AddHelper(fullPath, fileName);
    historyList.Add(rt);
  }


  void AddHistoryItemsForFiles(string[] fullPaths)
  {
    if (GlobalData.AreEffectsUnderway())
      return;

    var manualListItems = new List<RectTransform>();
    var autosaveListItems = new List<RectTransform>();

    foreach (var fullPath in fullPaths)
    {
      // Path.GetFileNameWithoutExtension can only throw ArgumentException
      // for the path having invalid characters, and AddHelper will only be
      // called after ValidateDirectoryName has cleared the path
      var fileName = Path.GetFileNameWithoutExtension(fullPath);
      var autosave = fileName.StartsWith("Auto");

      // because fullPaths is already sorted chronologically at this point,
      // we can just get the hell out of Dodge as soon as we hit the limit
      if (autosave)
      {
        if (m_CurrentAutosaveCount < m_MaxAutosaveCount)
        {
          ++m_CurrentAutosaveCount;
        }
        else
        {
          Debug.LogWarning($"Max autosave limit of {m_MaxAutosaveCount} reached when loading files. Aborting.");
          break;
        }
      }

      var rt = AddHelper(fullPath, fileName);
      var listToAddTo = autosave ? autosaveListItems : manualListItems;
      listToAddTo.Add(rt);
    }

    m_ManualSaveList.Add(manualListItems);
    m_AutosaveList.Add(autosaveListItems);
  }


  RectTransform AddHelper(string fullPath, string fileName)
  {
    var listItem = Instantiate(m_HistoryItemPrefab);

    listItem.Setup(this, fullPath, fileName);
    var rt = listItem.GetComponent<RectTransform>();

    return rt;
  }


  string GenerateFileName(bool autosave)
  {
    var now = DateTime.Now;
    var nowString = now.ToString(s_DateTimeFormat);
    var saveTypeString = autosave ? "Auto" : "Manual";
    var fileName = $"{saveTypeString} {nowString}{s_FilenameExtension}";

    return fileName;
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


  protected class StringCompression {
    // Compresses the input data using GZip
    public static byte[] Compress(string input)
    {
      byte[] byteArray = System.Text.Encoding.UTF8.GetBytes(input);

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
  public class MainThreadDispatcher {
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

  string FindDiff(Dictionary<Vector2Int, TileGrid.Element> currentGrid, Dictionary<Vector2Int, TileGrid.Element> oldGrid)
  {
    // Loop though both grids in tilegrid (new and old)
    // If there is a tile in new but not old, add `+ {json}` to changelist
    // If the is a tile in the old but not new, add `- {json}` to changelist
    // If on both, ignore.
    // TODO, redo load to use the new version saving.
    // TODO, redo save to use this to only save the diffrences <--
    // TODO, add are you sure, if you load a level with unsaved changes.
    // TODO, fix area placement taking forever
    // TODO, Large level creation and deletion still takes a long time.
    // TODO, game exit or file load "Are you sure" when there are unsaved changes or no file is mounted
    // TODO, store the m_latestLevelVersion when loading a level
    // TODO, make sure that when the mounted file is set to null, the level version is set to 0

    StringBuilder diffrences = new();
    diffrences.AppendLine("+");

    bool same;
    foreach (var kvp1 in currentGrid)
    {
      Vector2Int position = kvp1.Key;
      TileGrid.Element obj1 = kvp1.Value;

      if (oldGrid.TryGetValue(position, out TileGrid.Element obj2))
      {
        same = obj1.Equals(obj2);

        // Removed element so we don't check it again in the next loop
        oldGrid.Remove(position);

        if (same)
          continue;
      }
      diffrences.AppendLine(JsonUtility.ToJson(obj1));
    }

    diffrences.AppendLine("-");

    // Every tile left in the old grid will be removed
    foreach (var kvp2 in oldGrid)
    {
      diffrences.AppendLine(JsonUtility.ToJson(kvp2.Key));
    }

    return diffrences.ToString();
  }
}








