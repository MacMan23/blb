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
  [SerializeField]
  private RawImage m_SelectBG;
  [SerializeField]
  private AutoSaveInfo m_AutoSaveInfo;
  [SerializeField]
  private ManualSaveInfo m_ManualSaveInfo;

  private bool m_IsExpanded = true;
  private float m_LastPressedTime = 0;

  private static float s_DoublePressTime = 0.3f;


  [Serializable]
  private class AutoSaveInfo
  {
    public GameObject m_BranchCap;
    public GameObject m_BranchExtend;
    public RawImage m_BoxBG;
  }

  [Serializable]
  private class ManualSaveInfo
  {
    public RectTransform m_Arrow;
  }

  public void Init(FileSystem.LevelData levelData)
  {
    m_levelData = levelData;
    m_VersionName.text = levelData.m_Name;
    m_VersionData.text = ((DateTime)levelData.m_TimeStamp).ToString("M/d/yy h:mmtt").ToLower();
    if (!IsManualSave())
      m_VersionData.text = ((DateTime)levelData.m_TimeStamp).ToString("M/d/yy h:mm:sstt").ToLower();
  }

  public bool IsManualSave()
  {
    return m_levelData.m_BranchVersion == 0;
  }

  // Sets this autosave as the end cap to the autosave list
  public void SetLastAutoSave()
  {
    if (IsManualSave()) return;
    m_AutoSaveInfo.m_BranchCap.SetActive(true);
    m_AutoSaveInfo.m_BranchExtend.SetActive(false);
  }

  // Sets this autosave as a T branch, extened for more autosaves below
  private void SetInterposedAutoSave()
  {
    if (IsManualSave()) return;
    m_AutoSaveInfo.m_BranchCap.SetActive(false);
    m_AutoSaveInfo.m_BranchExtend.SetActive(true);
  }

  private void LoadVersion()
  {
    // TODO
  }

  public void Select()
  {
    // If we double clicked on this item
    if (Time.time - m_LastPressedTime <= s_DoublePressTime)
    {
      LoadVersion();
      return;
    }

    m_LastPressedTime = Time.time;

    bool selecting = false;
    for (int i = 0; i < transform.parent.childCount; i++)
    {
      var child = transform.parent.GetChild(i);
      if (child != null && child.TryGetComponent<UiHistoryItem>(out var item))
      {
        if (i == transform.GetSiblingIndex())
        {
          // If we are selecting an auto save, we only need to select this one
          // And select the whole bg too, not just the branch pannel
          if (!IsManualSave())
          {
            SetSelected();
            m_AutoSaveInfo.m_BoxBG.color = new Color32(82, 111, 155, 255);
            continue;
          }
          else
            selecting = true;
        }

        // If we reach the next autosave after selecting our autosave, stop selecting
        if (selecting && i > transform.GetSiblingIndex() && item.IsManualSave())
          selecting = false;

        if (selecting)
        {
          item.SetSelected();
        }
        else
        {
          item.SetUnselected();
        }
      }
    }

    // TODO: Update preview tab
  }

  private void SetSelected()
  {
    if (IsManualSave())
      m_SelectBG.color = new Color32(82, 111, 155, 255);
    else
    {
      m_SelectBG.color = new Color32(67, 73, 122, 255);
      m_AutoSaveInfo.m_BoxBG.color = new Color32(64, 64, 64, 255);
    }
  }

  private void SetUnselected()
  {
    if (IsManualSave())
      m_SelectBG.color = new Color32(75, 75, 75, 255);
    else
    {
      m_SelectBG.color = new Color32(34, 34, 34, 255);
      m_AutoSaveInfo.m_BoxBG.color = new Color32(64, 64, 64, 255);
    }
  }

  public bool IsExpanded()
  {
    return m_IsExpanded;
  }

  /// <summary> Toggle-activate branched autosaves and rotate arrow </summary>
  public void ToggleExpand()
  {
    if (!IsManualSave()) return;

    m_IsExpanded = !m_IsExpanded;
    if (m_IsExpanded)
      m_ManualSaveInfo.m_Arrow.rotation = Quaternion.Euler(0f, 0f, 180f);
    else
      m_ManualSaveInfo.m_Arrow.rotation = Quaternion.Euler(0f, 0f, -90f);

    // Loop and turn off all other autosaves
    for (int i = transform.GetSiblingIndex() + 1; i < transform.parent.childCount; i++)
    {
      var child = transform.parent.GetChild(i);
      if (child != null && child.TryGetComponent<UiHistoryItem>(out var item))
      {
        if (item.IsManualSave())
          break;
        else
          child.gameObject.SetActive(m_IsExpanded);
      }
    }
  }

  public int CompareTo(UiHistoryItem other)
  {
    // Sorts Largest to Smallest/Top to Bottom
    // -# = This goes up
    // +# = This goes down
    // == This stays

    bool thisIsManual = IsManualSave();
    bool otherIsManual = other.IsManualSave();

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

// TODO: Button highlight doesn't work on non selected auto saves. Maybe the button color is too dark to be tinted?
// TODO: Version Renaming