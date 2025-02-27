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

    // TODO: Remove lines for last auto save
  }

  public int CompareTo(UiHistoryItem other)
  {
    // Sorts Largest to Smallest/Top to Bottom
    // -# = This goes up
    // +# = This goes down
    // == This stays

    bool thisIsManual = m_levelData.m_BranchVersion == 0;
    bool otherIsManual = other.m_levelData.m_BranchVersion == 0;

    if (thisIsManual && otherIsManual)
      return other.m_levelData.m_Version - m_levelData.m_Version;

    if (thisIsManual)
    {
      int val = other.m_levelData.m_BranchVersion - m_levelData.m_Version;
      // If we return 0 they might stay on top of their branched version, so return -1 to move us up
      return val == 0 ? -1 : val;
    }

    if (otherIsManual)
    {
      int val = other.m_levelData.m_Version - m_levelData.m_BranchVersion;
      // If we return 0 we might stay on top of our branched version, so return 1 to move down
      return val == 0 ? 1 : val;
    }

    // Both are auto saves
    return (other.m_levelData.m_BranchVersion != m_levelData.m_BranchVersion)
        ? other.m_levelData.m_BranchVersion - m_levelData.m_BranchVersion
        : other.m_levelData.m_Version - m_levelData.m_Version;
  }

}
