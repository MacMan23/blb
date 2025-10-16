/***************************************************
Authors:        Douglas Zwick, Brenden Epp
Last Updated:   3/24/2025

Copyright 2018-2025, DigiPen Institute of Technology
***************************************************/

using B83.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using UnityEngine.Video;
using static FileDirUtilities;
using static FileVersioning;

public class FileSystemInternal : MonoBehaviour
{
  readonly static public string s_DateTimeFormat = "h-mm-ss.ff tt, ddd d MMM yyyy";
  readonly static public int s_MaxAutoSaveCount = 20;
  readonly static public int s_MaxManualSaveCount = 100;
  readonly static bool s_ShouldCompress = false;
  static string s_EditorVersion;

  public string m_DefaultDirectoryName = "Default Project";
  public TileGrid m_TileGrid;
  public FileDirUtilities m_FileDirUtilities;

  // Thumbnail size in pixels
  public Vector2Int m_ThumbnailSize = new(64, 64);
  public Sprite m_ThumbnailTileAtlas;
  /// <summary>
  /// Size in pixels of the small tiles used to compose a thumbnail.
  /// Derived from the size of the thumbnail tile atlas, assuming a square tile.
  /// This is done in Start, so don't expect this value to be set before then.
  /// </summary>
  private Vector2Int m_ThumbnailTileSize;

  private List<ThumbnailTile> m_ThumbnailTiles = new();

  UnityDragAndDropHook m_DragAndDropHook;

  ModalDialogMaster m_ModalDialogMaster;
  [SerializeField]
  protected ModalDialogAdder m_OverrideDialogAdder;
  [SerializeField]
  protected ModalDialogAdder m_SaveAsDialogAdder;
  [SerializeField]
  protected ModalDialogAdder m_ExportAsDialogAdder;

  // TODO Remove after thumbnail loading is done
  [SerializeField]
  private List<Texture2D> m_TempThumbnailImages;

  protected string m_PendingSaveFullFilePath = "";
  protected FileData m_PendingExportFileData = null;
  protected List<FileVersion> m_PendingExportVersions = null;

  private string m_PendingThumbnail = "";

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
    public uint m_LastId;
    public string m_Description;
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
    public uint m_Id;
    public string m_Thumbnail;
    public JsonDateTime m_TimeStamp;
    public List<TileGrid.Element> m_AddedTiles;
    public List<Vector2Int> m_RemovedTiles;
  }
  #endregion

  public class ThumbnailTile
  {
    public Color[] m_ColorData;

    public ThumbnailTile(TileType tileType, Color[] atlasBuffer,
      int atlasWidth, Vector2Int tileSize)
    {
      m_ColorData = new Color[tileSize.x * tileSize.y];
      // The buffer contains the pixel colors starting from the bottom-left
      //   corner of the texture. I believe it then goes to the right, and when
      //   it reaches the end of a row, it goes back to the start of the next
      //   row up from there.
      // startIndex is the index of the bottom-left pixel in the tile.
      var startIndex = (int)tileType * tileSize.x;
      // This is the index for writing to the thumbnail tile, incremented
      //   manually in the loop, so it's separate from the x and y indices that
      //   are only used to read from the atlas.
      var dataIndex = 0;

      for (var y = 0; y < tileSize.y; ++y)
      {
        for (var x = 0; x < tileSize.x; ++x)
        {
          var color = atlasBuffer[startIndex + x + y * atlasWidth];
          m_ColorData[dataIndex] = color;
          ++dataIndex;
        }
      }
    }
  }

  // Start is called before the first frame update
  void Start()
  {
    s_EditorVersion = Application.version;
    m_ModalDialogMaster = FindObjectOfType<ModalDialogMaster>();

    m_FileDirUtilities.SetDirectoryName(m_DefaultDirectoryName);

    var tileHeight = (int)m_ThumbnailTileAtlas.rect.height;
    m_ThumbnailTileSize = new Vector2Int(tileHeight, tileHeight);
    GenerateThumbnailTiles();
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
      m_FileDirUtilities.SetDirectoryName(m_DefaultDirectoryName);
    }
  }

  private void OnApplicationQuit()
  {
    // Force autosave if we have changes.
    bool isAutoSave = true;
    bool shouldPrintElapsedTime = false;
    Save(isAutoSave, null, shouldPrintElapsedTime);
  }

  private void CreateEmptyTempFile()
  {
    // This first save will be an empty manual
    CreateFileInfo(out m_MountedFileInfo);

    string destFilePath = m_FileDirUtilities.CreateTempFileName();
    m_MountedFileInfo.m_IsTempFile = true;
    m_MountedFileInfo.m_FileData.m_ManualSaves.Add(new LevelData());
    m_MountedFileInfo.m_FileData.m_ManualSaves[0].m_TimeStamp = DateTime.Now;
    // Mount temp file so we can check when the full file path isn't the temp file
    MountFile(destFilePath, m_MountedFileInfo);
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

  /// <summary>
  /// Prepares the thumbnail tile strip data in a format that can be used more rapidly for
  /// thumbnail generation. Called in Start. Don't call this function, Brenden.
  /// </summary>
  private void GenerateThumbnailTiles()
  {
    var atlasBuffer = m_ThumbnailTileAtlas.texture.GetPixels();
    var tileSize = m_ThumbnailTileSize;
    var atlasWidth = atlasBuffer.Length / tileSize.y;

    foreach (TileType tileType in Enum.GetValues(typeof(TileType)))
    {
      var thumbnailTile = new ThumbnailTile(tileType, atlasBuffer,
        atlasWidth, tileSize);
      m_ThumbnailTiles.Add(thumbnailTile);
    }
  }

  private string GenerateThumbnail(Dictionary<Vector2Int, TileGrid.Element> _grid)
  {
    // TODO, code to generate thumbnail
    // Texutre needs to be uncompressed and marked for read/write (Might be diffrent if the image is generated)

    var tex = new Texture2D(m_ThumbnailSize.x, m_ThumbnailSize.y, TextureFormat.RGBA32, false);
    var colorBuffer = new Color[m_ThumbnailSize.x * m_ThumbnailSize.y];

    var cameraPosition = Camera.main.transform.position;

    // thumbnail cols and rows is the width and height IN TILES of the thumbnail
    var thumbnailCols = m_ThumbnailSize.x / m_ThumbnailTileSize.x;
    var thumbnailRows = m_ThumbnailSize.y / m_ThumbnailTileSize.y;

    // Start with the camera position as the center of the thumbnail region.
    //   (Of course it won't be exactly in the center if we're using an
    //   odd-numbered thumbnail size.)
    // Count from the center to the left by half the width and down by half the
    //   height to find the tile index of the bottom-left tile in the thumbnail
    //   region. Then use a nested loop to look at each tile in that region and
    //   draw the thumbnail.

    var bottomLeftX = (int)(cameraPosition.x - thumbnailCols / 2);
    var bottomLeftY = (int)(cameraPosition.y - thumbnailRows / 2);

    // Lambdas for use in the loop here, so I don't have to do a ton of ifs in
    //   a deeply nested loop
    Func<int, int, int> defaultX = (x, y) => x;
    Func<int, int, int> defaultY = (x, y) => y;
    Func<int, int, int> flipX = (x, y) => m_ThumbnailTileSize.x - 1 - x;
    Func<int, int, int> flipY = (x, y) => m_ThumbnailTileSize.y - 1 - y;

    for (var row = 0; row < thumbnailRows; ++row)
    {
      for (var col = 0; col < thumbnailCols; ++col)
      {
        var tileIndex = new Vector2Int(bottomLeftX + col, bottomLeftY + row);
        var tileExists = _grid.TryGetValue(tileIndex, out var tile);
        var tileType = tileExists ? tile.m_Type : TileType.EMPTY;
        var tileDir = tileExists ? tile.m_Direction : Direction.RIGHT;

        var xTransformer = defaultX;
        var yTransformer = defaultY;

        // If the tile is a start or goon tile that's facing left rather
        //   than right, then it should be flipped in the thumbnail.
        if (tileType == TileType.START || tileType == TileType.GOON)
        {
          if (tileDir == Direction.LEFT)
          {
            xTransformer = flipX;
            yTransformer = defaultY;
          }
        }
        // Otherwise, if the tile is rotated to face some direction
        //   other than right, then it should be rotated in the thumbnail.
        else if (tileDir == Direction.UP)
        {
          xTransformer = flipY;
          yTransformer = defaultX;
        }
        else if (tileDir == Direction.LEFT)
        {
          xTransformer = flipX;
          yTransformer = flipY;
        }
        else if (tileDir == Direction.DOWN)
        {
          xTransformer = defaultY;
          yTransformer = flipX;
        }

        var tileColor = tileExists ? tile.m_TileColor : TileColor.RED;
        var actualColor = Color.white;

        // TODO: Bluaaghhghhh..... add "NONE" to the TileColor enum and hope
        //   it doesn't break everything. If I had that, then it would be
        //   the default color for everything that isn't colorable, and I
        //   wouldn't need an exhaustive list of all colorable tiles here
        if (tileType == TileType.TELEPORTER || tileType == TileType.DOOR ||
          tileType == TileType.KEY || tileType == TileType.SWITCH)
        {
          actualColor = ColorCode.Colors[tileColor];
        }

        var thumbnailTile = m_ThumbnailTiles[(int)tileType];

        for (var y = 0; y < m_ThumbnailTileSize.y; ++y)
        {
          for (var x = 0; x < m_ThumbnailTileSize.x; ++x)
          {
            var colorDataIndex = x + y * m_ThumbnailTileSize.x;
            // xSub and ySub are the x and y offsets within the tile
            var xSub = xTransformer(x, y);
            var ySub = yTransformer(x, y);
            
            var bufferIndex = col * m_ThumbnailTileSize.x + xSub +
              (row * m_ThumbnailTileSize.y + ySub) * m_ThumbnailSize.x;

            var color = thumbnailTile.m_ColorData[colorDataIndex];

            colorBuffer[bufferIndex] = color * actualColor;
          }
        }
      }
    }

    tex.SetPixels(colorBuffer);
    tex.Apply();

    byte[] bytes = tex.EncodeToPNG();
    return Convert.ToBase64String(bytes);
  }

  public static FileInfo SetFileDescriptionEx(string fullFilePath, string desc)
  {
    FileSystem.Instance.GetFileInfoFromFullFilePath(fullFilePath, out FileInfo fileInfo);

    fileInfo.m_FileData.m_Description = desc;

    return fileInfo;
  }

  protected void Save(bool autosave, string saveAsFileName = null, bool shouldPrintElapsedTime = true)
  {
    if (GlobalData.AreEffectsUnderway())
      return;

    m_TileGrid.GetLevelData(out LevelData levelData);
    // Check if we are going to save an empty level
    if (levelData.m_AddedTiles.Count == 0)
    {
      var errorString = "Failed to save because the level is empty";
      m_MainThreadDispatcher.Enqueue(() => StatusBar.Print(errorString));
      Debug.Log(errorString);
      return;
    }

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
      destFilePath = m_FileDirUtilities.CreateFilePath(saveAsFileName);

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
          // If this is an auto save and if we did saved recently (by checking if any operations were performed after the last save)
          if (autosave && OperationSystem.s_OperationCounterPublic == 0)
          {
            return;
          }

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
            m_FileDirUtilities.RemoveFileItem(m_MountedFileInfo.m_SaveFilePath);
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
          CreateEmptyTempFile();
          destFilePath = m_MountedFileInfo.m_SaveFilePath;
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

    // Gernerate the version thumbnail to be used in the thread
    // EncodeToPNG can only be used on main thread
    m_PendingThumbnail = GenerateThumbnail(m_TileGrid.GetGridBuffer());

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
    levelData.m_Thumbnail = m_PendingThumbnail;

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

    levelData.m_Thumbnail = m_PendingThumbnail;

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
    // Only write if we have differences
    if (hasDifferences)
    {
      // #6, 1, 3, 5, 8
      // We have data to add/overwite to any file
      levelData.m_TimeStamp = DateTime.Now;
      levelData.m_Id = ++m_MountedFileInfo.m_FileData.m_LastId;

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
        // Set the manual save version we are branching off from
        // Get the manual version or the branched manual version if we loaded an auto save
        levelData.m_Version.m_ManualVersion = m_loadedVersion.m_ManualVersion;

        // Check if there are other autosaves branched from this manual
        // If so, our version will be 1 more than the newest one
        int lastVersion = GetLastAutoSaveVersion(m_MountedFileInfo.m_FileData, m_loadedVersion.m_ManualVersion);
        levelData.m_Version.m_AutoVersion = lastVersion + 1;

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
      m_MainThreadDispatcher.Enqueue(() => m_FileDirUtilities.MoveFileItemToTop(fullFilePath));
    else
      m_MainThreadDispatcher.Enqueue(() => m_FileDirUtilities.AddFileItemForFile(fullFilePath));
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
      var durationStr = $"{(duration.Hours > 0 ? $"{duration.Hours}h " : "")}" +
                        $"{(duration.Minutes > 0 ? $"{duration.Minutes}m " : "")}" +
                        $"{Math.Round(duration.TotalSeconds % 60.0, 2)}s";

      var mainColor = "#ffffff99";
      var fileColor = isAutosave ? "white" : "yellow";
      var timeColor = "#ffffff66";

      m_MainThreadDispatcher.Enqueue(() =>
        StatusBar.Print($"<color={mainColor}>Saved</color> " +
                        $"<color={fileColor}>{Path.GetFileName(destFilePath)}</color> " +
                        $"<color={timeColor}>in {durationStr}</color>")
      );

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

    // Save happened reset operations so we don't autosave again right away
    OperationSystem.ResetOperationCounter();
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

    m_FileDirUtilities.MoveFileItemToTop(fileInfo.m_SaveFilePath);

    StatusBar.Print($"Sucessfuly deleted {versionDescription} from {fileInfo.m_SaveFilePath}");
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

// These three are the same:
// TODO, game exit or file load "Are you sure" when there are unsaved changes or no file is mounted
// TODO, add are you sure, if you load a level with unsaved changes.
// TODO, When closeing app, ask to save if there are unsaved changes.
// If no, delete temp file on close
// OOOR we could just autosave to the latest manual


// __Needs UI__
// Unsaved changes prompt
// Warning about max manual saves reached

// TODO: Be able to delete a file from the save files bar.
