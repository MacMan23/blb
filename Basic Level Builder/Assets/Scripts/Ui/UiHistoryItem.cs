using System;
using UnityEngine;
using UnityEngine.UI;

public class UiHistoryItem : MonoBehaviour
{
  private FileSystem.LevelData m_levelData;
  [SerializeField]
  private Image m_ThumbnailImage;
  [SerializeField]
  private TMPro.TextMeshProUGUI m_VersionName;
  [SerializeField]
  private TMPro.TextMeshProUGUI m_VersionData;

  public void Init(FileSystem.LevelData levelData)
  {
    m_levelData = levelData;
    m_VersionName.text = levelData.m_Name;
    m_VersionData.text = ((DateTime)levelData.m_TimeStamp).ToString("g");
  }
}
