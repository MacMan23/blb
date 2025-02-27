using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UiFileInfo : MonoBehaviour
{
  private string m_fileFullPath;
  [SerializeField]
  private GameObject m_fileContainer;

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

  }

  // Update is called once per frame
  void Update()
  {

  }

  public void InitLoad(string fullFilePath)
  {
    m_fileFullPath = fullFilePath;

    // Create all the history items

  }
}
