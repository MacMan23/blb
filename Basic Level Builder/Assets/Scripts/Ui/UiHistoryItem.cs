/***************************************************
Authors:        Brenden Epp
Last Updated:   3/24/2025

Copyright 2018-2025, DigiPen Institute of Technology
***************************************************/

using System;
using UnityEngine;
using UnityEngine.UI;
using static FileVersioning;

public class UiHistoryItem : MonoBehaviour
{
  private readonly static Color s_SelectedManualSaveColor = new Color32(82, 111, 155, 255);
  private readonly static Color s_SelectedAutoSaveColor = new Color32(82, 111, 155, 255);
  private readonly static Color s_SelectedAutoSaveBranchColor = new Color32(67, 73, 122, 255);
  private readonly static Color s_UnselectedManualSaveColor = new Color32(75, 75, 75, 255);
  private readonly static Color s_UnselectedAutoSaveColor = new Color32(64, 64, 64, 255);
  private readonly static Color s_UnselectedAutoSaveBranchColor = new Color32(34, 34, 34, 255);

  readonly static private string s_AutoSaveName = "Auto ";
  readonly static private string s_ManualSaveName = "Version ";

  public delegate void SelectAction(UiHistoryItem item);
  public static event SelectAction OnSelected;
  public delegate void CloseInfoWindowAction();
  public static event CloseInfoWindowAction OnCloseInfoWindow;

  private FileSystemInternal.LevelData m_LevelData;
  private string m_FullFilePath;

  [SerializeField]
  private Image m_ThumbnailImage;
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
    public TMPro.TextMeshProUGUI m_VersionName;
    public GameObject m_BranchCap;
    public GameObject m_BranchExtend;
    public RawImage m_BoxBG;
  }

  [Serializable]
  private class ManualSaveInfo
  {
    public TMPro.TMP_InputField m_VersionInputName;
    public RectTransform m_Arrow;
  }

  public void Init(FileSystemInternal.LevelData levelData, string fullFilePath)
  {
    m_LevelData = levelData;
    m_FullFilePath = fullFilePath;

    if (string.IsNullOrEmpty(levelData.m_Name))
    {
      if (IsManualSave())
        m_ManualSaveInfo.m_VersionInputName.text = s_ManualSaveName + levelData.m_Version.m_ManualVersion + " ID: " + levelData.m_Id;
      else
        m_AutoSaveInfo.m_VersionName.text = "<i>" + s_AutoSaveName + levelData.m_Version.m_AutoVersion + " ID: " + levelData.m_Id;
    }
    else
    {
      if (IsManualSave())
        m_ManualSaveInfo.m_VersionInputName.text = levelData.m_Name;
      else
        m_AutoSaveInfo.m_VersionName.text = levelData.m_Name;
    }

    m_VersionData.text = GetVersionTimeStamp();

    byte[] bytes = Convert.FromBase64String(levelData.m_Thumbnail);
    Texture2D tex = new(0, 0); // No real reason for the width/height values in the constructor, they will be overwritten anyways in LoadImage
    tex.LoadImage(bytes);

    Sprite sprite = Sprite.Create(
            tex,
            new Rect(0, 0, tex.width, tex.height),
            new Vector2(0.5f, 0.5f) // pivot in the center
        );

    m_ThumbnailImage.sprite = sprite;
  }

  public void EditName()
  {
    m_ManualSaveInfo.m_VersionInputName.ActivateInputField();
  }

  public void SetName()
  {
    FileSystem.Instance.SetVersionName(m_FullFilePath, m_LevelData.m_Version, m_ManualSaveInfo.m_VersionInputName.text);
  }

  public bool IsManualSave()
  {
    return m_LevelData.m_Version.IsManual();
  }

  public FileVersion GetVersion()
  {
    return m_LevelData.m_Version;
  }

  public uint GetId()
  {
    return m_LevelData.m_Id;
  }

  public string GetFilePath()
  {
    return m_FullFilePath;
  }

  public string GetVersionName()
  {
    string name = m_LevelData.m_Name;
    if (string.IsNullOrEmpty(name))
    {
      if (IsManualSave())
        name = s_ManualSaveName + m_LevelData.m_Version.m_ManualVersion;
      else
        name = "Br : " + m_LevelData.m_Version.m_ManualVersion + " " + s_AutoSaveName + m_LevelData.m_Version.m_AutoVersion;
    }

    return name;
  }

  public Sprite GetThumbnail()
  {
    return m_ThumbnailImage.sprite;
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
    FileSystem.Instance.LoadFromFullFilePath(m_FullFilePath, m_LevelData.m_Version);
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

    // Notify listeners that this item was selected
    OnSelected?.Invoke(this);
  }

  public void SetColorAndAutosAsSelected()
  {
    // Sets this elements color
    SetColorAsSelected();

    // We don't need to do any more if we are just selecting an autosave
    if (!IsManualSave())
      return;


    for (int i = 0; i < transform.parent.childCount; i++)
    {
      var child = transform.parent.GetChild(i);
      if (child == null || !child.TryGetComponent(out UiHistoryItem item))
        continue;

      if (i <= transform.GetSiblingIndex())
        continue;

      // If we reach the next manual save after selecting our last item, stop selecting
      if (i > transform.GetSiblingIndex() && item.IsManualSave())
        return;

      // Select all autosaves attached to this manual
      item.m_SelectBG.color = s_SelectedAutoSaveBranchColor;
    }
  }

  private void SetColorAsSelected()
  {
    if (IsManualSave())
      m_SelectBG.color = s_SelectedManualSaveColor;
    else
    {
      m_SelectBG.color = s_SelectedAutoSaveBranchColor;
      m_AutoSaveInfo.m_BoxBG.color = s_SelectedAutoSaveColor;
    }
  }

  public void SetColorAndAutosAsUnselected(bool isManualSelected = false)
  {
    // Sets this elements color
    SetColorAsUnselected(isManualSelected);

    // We don't need to do any more if we are just selecting an autosave
    if (!IsManualSave())
      return;


    for (int i = 0; i < transform.parent.childCount; i++)
    {
      var child = transform.parent.GetChild(i);
      if (child == null || !child.TryGetComponent(out UiHistoryItem item))
        continue;

      if (i <= transform.GetSiblingIndex())
        continue;

      // If we reach the next manual save after selecting our last item, stop selecting
      if (i > transform.GetSiblingIndex() && item.IsManualSave())
        return;

      // Unselects the branch color of all autosaves attached to this manual
      if (item.m_AutoSaveInfo.m_BoxBG.color == s_UnselectedAutoSaveColor)
        item.m_SelectBG.color = s_UnselectedAutoSaveBranchColor;
    }
  }

  public void SetColorAsUnselected(bool isManualSelected = false)
  {
    if (IsManualSave())
      m_SelectBG.color = s_UnselectedManualSaveColor;
    else
    {
      if (!isManualSelected)
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
    return GetVersion().CompareTo(other.GetVersion());
  }
}