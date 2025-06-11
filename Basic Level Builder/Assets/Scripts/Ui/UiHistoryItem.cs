/***************************************************
Authors:        Brenden Epp
Last Updated:   3/24/2025

Copyright 2018-2025, DigiPen Institute of Technology
***************************************************/

using System;
using UnityEngine;
using UnityEngine.UI;

public class UiHistoryItem : MonoBehaviour
{
  private readonly static Color s_SelectedManualSaveColor = new Color32(82, 111, 155, 255);
  private readonly static Color s_SelectedAutoSaveColor = new Color32(82, 111, 155, 255);
  private readonly static Color s_SelectedAutoSaveBranchColor = new Color32(67, 73, 122, 255);
  private readonly static Color s_UnselectedManualSaveColor = new Color32(75, 75, 75, 255);
  private readonly static Color s_UnselectedAutoSaveColor = new Color32(64, 64, 64, 255);
  private readonly static Color s_UnselectedAutoSaveBranchColor = new Color32(34, 34, 34, 255);

  public delegate void SelectAction(UiHistoryItem item);
  public static event SelectAction OnSelected;
  public delegate void CloseInfoWindowAction();
  public static event CloseInfoWindowAction OnCloseInfoWindow;

  private FileSystem.LevelData m_LevelData;
  private string m_FullFilePath;

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
  private float m_LastPressedTime = float.MinValue;

  private static readonly float s_DoublePressTime = 0.3f;


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

  public void Init(FileSystem.LevelData levelData, string fullFilePath)
  {
    m_LevelData = levelData;
    m_FullFilePath = fullFilePath;
    if (IsManualSave())
      m_VersionName.text = levelData.m_Name;
    else
      m_VersionName.text = "<i>" + levelData.m_Name;
    m_VersionData.text = GetVersionTimeStamp();
  }

  public bool IsManualSave()
  {
    return m_LevelData.m_BranchVersion == 0;
  }

  public int GetVersion()
  {
    return m_LevelData.m_Version;
  }

  public int GetBranchVersion()
  {
    return m_LevelData.m_BranchVersion;
  }

  public string GetFilePath()
  {
    return m_FullFilePath;
  }

  public string GetVersionName()
  {
    return m_LevelData.m_Name;
  }

  public string GetVersionTimeStamp()
  {
    return ((DateTime)m_LevelData.m_TimeStamp).ToString("M/d/yy h:mm:sstt").ToLower();
  }

  // Sets this autosave as the end cap to the autosave list
  public void SetLastAutoSave()
  {
    if (IsManualSave()) return;
    m_AutoSaveInfo.m_BranchCap.SetActive(true);
    m_AutoSaveInfo.m_BranchExtend.SetActive(false);
  }

  // Sets this autosave as a T branch, extened for more autosaves below
  [Obsolete]
  private void SetInterposedAutoSave()
  {
    if (IsManualSave()) return;
    m_AutoSaveInfo.m_BranchCap.SetActive(false);
    m_AutoSaveInfo.m_BranchExtend.SetActive(true);
  }

  public void Load()
  {
    FileSystem.Instance.LoadFromFullPath(m_FullFilePath, m_LevelData.m_Version, m_LevelData.m_BranchVersion);
    OnCloseInfoWindow?.Invoke();
  }

  public void Select()
  {
    // If we double clicked on this item
    if (Time.time - m_LastPressedTime <= s_DoublePressTime)
    {
      Load();
      return;
    }

    m_LastPressedTime = Time.time;

    bool selecting = false;
    for (int i = 0; i < transform.parent.childCount; i++)
    {
      var child = transform.parent.GetChild(i);
      if (child == null || !child.TryGetComponent(out UiHistoryItem item))
        continue;

      if (i == transform.GetSiblingIndex())
      {
        // If we are selecting an auto save, we only need to select this one
        // And select the whole bg too, not just the branch pannel
        if (!IsManualSave())
        {
          SetColorAsSelected();
          m_AutoSaveInfo.m_BoxBG.color = s_SelectedAutoSaveColor;
          continue;
        }
        else
          selecting = true;
      }

      // If we reach the next manual save after selecting our last item, stop selecting
      if (selecting && i > transform.GetSiblingIndex() && item.IsManualSave())
        selecting = false;

      if (selecting)
      {
        item.SetColorAsSelected();
      }
      else
      {
        // Only deselect last item if we aren't multiselecting
        if (!HotkeyMaster.IsMultiSelectHeld())
          item.SetColorAsUnselected();
      }
    }

    // Notify listeners that this item was selected
    OnSelected?.Invoke(this);
  }

  public void Deselect()
  {
    SetColorAsUnselected();
  }

  public void SetColorAsSelected()
  {
    if (IsManualSave())
      m_SelectBG.color = s_SelectedManualSaveColor;
    else
    {
      m_SelectBG.color = s_SelectedAutoSaveBranchColor;
      m_AutoSaveInfo.m_BoxBG.color = s_UnselectedAutoSaveColor;
    }
  }

  private void SetColorAsUnselected()
  {
    if (IsManualSave())
      m_SelectBG.color = s_UnselectedManualSaveColor;
    else
    {
      m_SelectBG.color = s_UnselectedAutoSaveBranchColor;
      m_AutoSaveInfo.m_BoxBG.color = s_UnselectedAutoSaveColor;
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

    // Rotate arrow
    if (m_IsExpanded)
      m_ManualSaveInfo.m_Arrow.rotation = Quaternion.Euler(0f, 0f, 180f);
    else
      m_ManualSaveInfo.m_Arrow.rotation = Quaternion.Euler(0f, 0f, -90f);

    // Loop and turn on/off all our autosaves
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

  public void SetArrowActive(bool state)
  {
    if (!IsManualSave()) return;

    m_ManualSaveInfo.m_Arrow.gameObject.SetActive(state);
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
      return other.m_LevelData.m_Version - m_LevelData.m_Version;

    if (thisIsManual)
    {
      int val = other.m_LevelData.m_BranchVersion - m_LevelData.m_Version;
      // If we return 0 they might stay on top of their branched version, so return -1 to move us up
      return val == 0 ? -1 : val;
    }

    if (otherIsManual)
    {
      int val = other.m_LevelData.m_Version - m_LevelData.m_BranchVersion;
      // If we return 0 we might stay on top of our branched version, so return 1 to move down
      return val == 0 ? 1 : val;
    }

    // Both are auto saves
    return (other.m_LevelData.m_BranchVersion != m_LevelData.m_BranchVersion)
        ? other.m_LevelData.m_BranchVersion - m_LevelData.m_BranchVersion
        : other.m_LevelData.m_Version - m_LevelData.m_Version;
  }
}

// TODO: Button highlight doesn't work on non selected auto saves. Maybe the button color is too dark to be tinted?
// TODO: Version Renaming