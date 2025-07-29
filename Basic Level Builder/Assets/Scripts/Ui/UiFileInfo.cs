/***************************************************
Authors:        Brenden Epp
Last Updated:   3/24/2025

Copyright 2018-2025, DigiPen Institute of Technology
***************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using static FileVersioning;

public class UiFileInfo : MonoBehaviour
{
  [SerializeField]
  private UiHistoryItem m_ManualSaveItemPrefab;
  [SerializeField]
  private UiHistoryItem m_AutoSaveItemPrefab;
  [SerializeField]
  private RectTransform m_Content;
  [SerializeField]
  private TMPro.TextMeshProUGUI m_versionInfoText;
  [SerializeField]
  private Image m_VersionInfoThumbnail;

  [SerializeField]
  private GameObject m_ExportButton;
  [SerializeField]
  private GameObject m_PromoteButton;
  [SerializeField]
  private GameObject m_LoadButton;
  [SerializeField]
  private GameObject m_DeleteButton;

  private List<UiHistoryItem> m_Selection = new();

  List<uint> m_ExpandedManuals = new();
  uint m_SelectedSave;

  // The first selected item to use when selecting in a range
  // This will update for as long as the range selection modifier is not held
  // If the modifier is held, it will select all items between this index and the next selected item
  private int m_RangeSelectionFirstIndex = 0;

  private string m_FullFilePath;

  void OnEnable()
  {
    UiHistoryItem.OnSelected += OnHistoryItemSelected;
    UiHistoryItem.OnCloseInfoWindow += CloseWindow;
  }

  void OnDisable()
  {
    UiHistoryItem.OnSelected -= OnHistoryItemSelected;
    UiHistoryItem.OnCloseInfoWindow -= CloseWindow;
  }

  public void InitLoad(string fullFilePath)
  {
    m_FullFilePath = fullFilePath;
    LoadHistoryItemList();
  }

  private void ClearHistoryItemList()
  {
    while (m_Content.childCount > 0)
    {
      DestroyImmediate(m_Content.GetChild(0).gameObject);
    }
  }

  private void LoadHistoryItemList()
  {
    try
    {
      // Create all the file items
      // Load the files data
      FileSystemInternal.FileInfo fileInfo;

      try
      {
        FileSystem.Instance.GetFileInfoFromFullFilePath(m_FullFilePath, out fileInfo);
      }
      catch (Exception e)
      {
        Debug.LogWarning($"Failed to get data from file path: {m_FullFilePath}. {e.Message}");
        StatusBar.Print($"Error: Could not load file history.");
        CloseWindow();
        return;
      }

      List<UiHistoryItem> items = new();

      foreach (var levelData in fileInfo.m_FileData.m_ManualSaves)
      {
        items.Add(CreateHistoryItem(levelData, m_FullFilePath, m_ManualSaveItemPrefab));
      }
      foreach (var levelData in fileInfo.m_FileData.m_AutoSaves)
      {
        items.Add(CreateHistoryItem(levelData, m_FullFilePath, m_AutoSaveItemPrefab));
      }

      if (items.Count == 0)
      {
        Debug.LogWarning($"No versions found in file: {m_FullFilePath}");
        StatusBar.Print($"Error: File empty");
        CloseWindow();
        return;
      }

      UpdateVersionList(items);

      // First time run will collapse all
      ToggleSaveExpansion();
    }
    catch (Exception e)
    {
      Debug.LogError($"Unexpected error loading file history: {e.Message} ({e.GetType()})");
      StatusBar.Print($"Error: Could not load file history due to an unexpected error.");
      CloseWindow();
    }
  }

  public void CloseWindow()
  {
    GameObject root = GameObject.FindGameObjectWithTag("FileInfoRoot");
    if (!root)
    {
      Debug.LogError("Could not find FileInfoRoot");
      return;
    }

    // Toggle on the black background
    root.GetComponent<Image>().enabled = false;
    GlobalData.DecreaseUiPopup();

    Destroy(gameObject);
  }

  private UiHistoryItem CreateHistoryItem(FileSystemInternal.LevelData levelData, string fullFilePath, UiHistoryItem prefab)
  {
    UiHistoryItem historyItem = Instantiate(prefab);
    // Give level data so it can init its text and thumbnail
    historyItem.Init(levelData, fullFilePath);
    // Add item to list view
    if (historyItem.TryGetComponent(out RectTransform rect))
    {
      rect.SetParent(m_Content);
    }
    return historyItem;
  }

  public void ToggleSaveExpansion()
  {
    // If we have no expanded saves, set to expand all
    bool shouldExpand = GetNumberOfExpandedSaves() == 0;

    // Loop all save versions
    for (int i = 0; i < m_Content.childCount; i++)
    {
      var child = m_Content.GetChild(i);
      // If the version isn't what we want, toggle expand, else don't
      if (child != null && child.TryGetComponent<UiHistoryItem>(out var item) &&
          item.IsManualSave() && item.IsExpanded() != shouldExpand)
      {
        item.ToggleExpand();
      }
    }
  }

  public void LoadSelectedVersion()
  {
    UiHistoryItem item = m_Selection[0];

    if (item)
      item.Load();
  }

  public void PromoteAutoSave()
  {
    if (m_Selection.Count <= 0)
    {
      throw new Exception("Promoting with no version selected");
    }

    // These should never be the case, but just to make sure...
    if (m_Selection.Count > 1 || m_Selection[0].GetVersion().IsManual())
      return;

    FileSystem.Instance.PromoteAutoSave(m_FullFilePath, m_Selection[0].GetVersion());

    RefreshUi();
  }

  public void ExportSelectedVersions()
  {
    if (m_Selection.Count <= 0)
    {
      throw new Exception("Exporting version(s) with no version(s) selected");
    }

    List<FileVersion> versions = new();
    foreach (var item in m_Selection)
    {
      versions.Add(item.GetVersion());
    }

    if (m_Selection.Count == 1)
      FileSystem.Instance.ExportVersion(m_Selection[0].GetFilePath(), m_Selection[0].GetVersion());
    else
      FileSystem.Instance.ExportMultipleVersions(m_Selection[0].GetFilePath(), versions);
  }

  // TODO: If deleting last manual save ask if want to delete whole file.
  // Or remove delete button if there is only one version left
  public void DeleteSelectedVersions()
  {
    // This shouldn't happen as the button wouldn't be visable if no version are selected
    if (m_Selection.Count <= 0)
    {
      throw new Exception("Deleting version(s) with no version(s) selected");
    }

    FileSystem.Instance.GetFileInfoFromFullFilePath(m_FullFilePath, out FileSystemInternal.FileInfo fileInfo);
    if (m_Selection.Count > 1)
    {
      List<FileVersion> versions = new();
      foreach (var item in m_Selection)
      {
        versions.Add(item.GetVersion());
      }

      FileSystem.Instance.DeleteMultipleVersions(fileInfo, versions);
    }
    else
    {
      FileSystem.Instance.DeleteVersion(fileInfo, m_Selection[0].GetVersion());
    }

    RefreshUi();
  }

  private void RefreshUi()
  {
    SaveUiState();
    ClearHistoryItemList();
    LoadHistoryItemList();
    LoadUiState();
    UpdateVersionInfo();
  }

  private void LoadUiState()
  {
    m_Selection.Clear();

    for (int i = 0; i < m_Content.childCount; i++)
    {
      var child = m_Content.GetChild(i);
      if (child != null && child.TryGetComponent<UiHistoryItem>(out var item))
      {
        if (item.GetId() == m_SelectedSave)
          item.Select();

        if (item.IsManualSave() && m_ExpandedManuals.Any(p => p == item.GetId()))
        {
          item.ToggleExpand();
          m_ExpandedManuals.Remove(item.GetId());
        }
      }

      // If we finished expanding all saves and selecting the last selected
      if (m_ExpandedManuals.Count == 0 && m_Selection.Count == 1)
        break;
    }
  }

  private void SaveUiState()
  {
    if (m_Selection.Count > 0)
      m_SelectedSave = m_Selection[0].GetId();
    else
      m_SelectedSave = 0;

    m_ExpandedManuals.Clear();
    for (int i = 0; i < m_Content.childCount; i++)
    {
      var child = m_Content.GetChild(i);
      if (child != null && child.TryGetComponent<UiHistoryItem>(out var item))
      {
        if (item.IsManualSave() && item.IsExpanded())
        {
          m_ExpandedManuals.Add(item.GetId());
        }
      }
    }
  }

  private int GetNumberOfExpandedSaves()
  {
    int num = 0;
    for (int i = 0; i < m_Content.childCount; i++)
    {
      var child = m_Content.GetChild(i);
      if (child != null && child.TryGetComponent<UiHistoryItem>(out var item) &&
          item.IsManualSave() && item.IsExpanded())
      {
        ++num;
      }
    }
    return num;
  }

  // Sorts, checks lone manual
  private void UpdateVersionList(List<UiHistoryItem> items = null)
  {
    // If no items were passed in, create our own list
    items ??= GetAllHistoryItems();

    // Properly sort items
    items.Sort((a, b) => a.CompareTo(b));

    // Set sorted items index in hierarchy
    for (int i = 0; i < items.Count; i++)
    {
      items[i].transform.SetSiblingIndex(i);

      // Is there a previous item?
      if (i > 0)
      {
        // Set the prev item as the last auto save in the list so the branch lines don'e continue
        // (SetLastAutoSave is ignored if that item is a manual too)
        if (items[i].IsManualSave())
        {
          items[i - 1].SetLastAutoSave();
        }

        // Check if the last item was a manual with no autos
        if (items[i].IsManualSave() && items[i - 1].IsManualSave())
        {
          items[i - 1].SetArrowActive(false);
        }
        else
        {
          items[i - 1].SetArrowActive(true);
        }
      }

      // If this is the last item
      if (i == items.Count - 1)
      {
        // If this is the last item and auto, set as the last auto save in the list
        if (!items[i].IsManualSave())
        {
          items[i].SetLastAutoSave();
        }
        else
        {
          // If we are the last item and manual save, we have no autos, so remove the arrow
          items[i].SetArrowActive(false);
        }
      }
    }
  }

  private List<UiHistoryItem> GetAllHistoryItems()
  {
    List<UiHistoryItem> items = new();

    // Loop all save versions
    for (int i = 0; i < m_Content.childCount; i++)
    {
      var child = m_Content.GetChild(i);
      if (child != null && child.TryGetComponent<UiHistoryItem>(out var item))
      {
        items.Add(item);
      }
    }

    return items;
  }

  private void ClearSelection()
  {
    foreach (var item in GetAllHistoryItems())
    {
      item.SetColorAsUnselected();
    }
    m_Selection.Clear();
  }

  private void OnHistoryItemSelected(UiHistoryItem selectedItem)
  {
    // Clear text if null
    if (selectedItem == null)
    {
      ClearSelection();
      return;
    }

    List<UiHistoryItem> items = GetAllHistoryItems();

    if (!HotkeyMaster.IsRangeSelectHeld())
      m_RangeSelectionFirstIndex = items.IndexOf(selectedItem);

    if (HotkeyMaster.IsRangeSelectHeld())
    {
      int endIndex = items.IndexOf(selectedItem);
      int startIndex = m_RangeSelectionFirstIndex;

      // Swap indexes if we are selecing the other side of the list
      if (startIndex > endIndex)
      {
        (startIndex, endIndex) = (endIndex, startIndex);
      }

      ClearSelection();
      for (int i = startIndex; i <= endIndex; i++)
      {
        AddToSelection(items[i]);
      }
    }
    else if (HotkeyMaster.IsMultiSelectHeld())
    {
      if (!m_Selection.Contains(selectedItem))
        AddToSelection(selectedItem);
      else
        RemoveFromSelection(selectedItem);
    }
    else
    {
      ClearSelection();
      AddToSelection(selectedItem);
    }

    m_Selection.Sort((a, b) => a.CompareTo(b));

    UpdateVersionInfo();
  }

  private void AddToSelection(UiHistoryItem item)
  {
    m_Selection.Add(item);
    item.SetColorAndAutosAsSelected();
  }

  private void RemoveFromSelection(UiHistoryItem item)
  {
    m_Selection.Remove(item);
    bool manualSelected = false;
    if (!item.GetVersion().IsManual())
    {
      foreach (UiHistoryItem item2 in m_Selection)
      {
        if (item2.GetVersion().m_ManualVersion == item.GetVersion().m_ManualVersion && item2.GetVersion().IsManual())
          manualSelected = true;
      }
    }
    item.SetColorAndAutosAsUnselected(manualSelected);
  }

  private void UpdateVersionInfo()
  {
    if (m_Selection.Count == 0)
    {
      m_versionInfoText.text = "<b>No version selected</b>\r\n";

      // Reenable buttons if they were gone before
      m_ExportButton.SetActive(false);
      m_LoadButton.SetActive(false);
      m_DeleteButton.SetActive(false);
      m_PromoteButton.SetActive(false);
    }
    else if (m_Selection.Count == 1)
    {
      m_versionInfoText.text = "<b>" + m_Selection[0].GetVersionName() + "</b>\r\n";
      m_versionInfoText.text += "<color=#C6C6C6>" + m_Selection[0].GetVersionTimeStamp() + "</color>";

      // Reenable buttons if they were gone before
      m_ExportButton.SetActive(true);
      m_LoadButton.SetActive(true);
      m_DeleteButton.SetActive(true);
      m_PromoteButton.SetActive(!m_Selection[0].GetVersion().IsManual());
    }
    else
    {
      m_versionInfoText.text = "<b>Multiple versions selected</b>\r\n";
      m_versionInfoText.text += "<color=#C6C6C6>" + m_Selection[0].GetVersionName();

      // Add all selected items up to 4 total
      if (m_Selection.Count < 5)
      {
        foreach (var item in m_Selection.GetRange(1, m_Selection.Count - 1))
        {
          m_versionInfoText.text += ", " + item.GetVersionName();
        }
      }
      else
      {
        // Only add an ellipsis and the last item if we have too many selected
        m_versionInfoText.text += " .... " + m_Selection[^1].GetVersionName();
      }

      // Remove the load buttons as we can't do that when multiple versions are selected
      m_ExportButton.SetActive(true);
      m_LoadButton.SetActive(false);
      m_DeleteButton.SetActive(true);
      m_PromoteButton.SetActive(false);
    }
  }
}