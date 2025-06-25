/***************************************************
Authors:        Brenden Epp
Last Updated:   3/24/2025

Copyright 2018-2025, DigiPen Institute of Technology
***************************************************/

using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;
using System;

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
  private GameObject m_LoadButton;
  [SerializeField]
  private GameObject m_DeleteButton;

  private List<UiHistoryItem> m_Selection = new();

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
      FileSystem.FileInfo fileInfo;

      try
      {
        FileSystem.Instance.GetDataFromFullPath(m_FullFilePath, out fileInfo);
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

      items[0].Select();
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

    Destroy(gameObject);
  }

  private UiHistoryItem CreateHistoryItem(FileSystem.LevelData levelData, string fullFilePath, UiHistoryItem prefab)
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
    bool shouldExpand = GetNumberExpandedSaves() == 0;

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

  public void ExportSelectedVersion()
  {
    UiHistoryItem item = m_Selection[0];

    if (item)
      FileSystem.Instance.ExportVersion(item.GetFilePath(), item.GetVersion());
  }

  // TODO: If deleting last manual save ask if want to delete whole file.
  // Or remove delete button if there is only one version left
  public void DeleteSelectedVersions()
  {
    // This shouldn't happen as the button wouldn't be visable if no version are selected
    if (m_Selection.Count <= 0)
    {
      throw new Exception("Deleting version with no versions selected");
    }

    FileSystem.Instance.GetDataFromFullPath(m_FullFilePath, out FileSystem.FileInfo fileInfo);
    if (m_Selection.Count > 1)
    {
      List<FileSystem.Version> versions = new();
      foreach (var item in m_Selection)
      {
        versions.Add(item.GetVersion());
      }

      FileSystem.Instance.DeleteVersions(fileInfo, versions);
    }
    else
    {
      FileSystem.Instance.DeleteVersion(fileInfo, m_Selection[0].GetVersion());
    }

    // We changed up a lot if we deleted a manual and its autos
    // Or if we deleted multiple files,
    // So delete and recreate the item list
    if (m_Selection.Count > 1 || m_Selection[0].IsManualSave())
    {
      ClearHistoryItemList();
      LoadHistoryItemList();
    }
    else
    {
      DestroyImmediate(m_Selection[0].gameObject);
      UpdateVersionList();
    }
    Deselect();
  }

  private int GetNumberExpandedSaves()
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

  private void Deselect()
  {
    OnHistoryItemSelected(null);

    foreach (var item in GetAllHistoryItems())
    {
      item.Deselect();
    }
  }

  private void OnHistoryItemSelected(UiHistoryItem selectedItem)
  {
    // Clear text if null
    if (selectedItem == null)
    {
      m_Selection.Clear();
      UpdateVersionInfo();
      return;
    }

    if (HotkeyMaster.IsMultiSelectHeld())
    {
      if (!m_Selection.Contains(selectedItem))
        m_Selection.Add(selectedItem);
    }
    else
    {
      m_Selection.Clear();
      m_Selection.Add(selectedItem);
    }

    m_Selection.Sort((a, b) => a.CompareTo(b));

    DeselectTwiceSelectedAutosaves();

    UpdateVersionInfo();
  }

  private void UpdateVersionInfo()
  {
    if (m_Selection.Count == 0)
    {
      m_versionInfoText.text = "<b>No Version Selected</b>\r\n";

      // Reenable buttons if they were gone before
      m_ExportButton.SetActive(false);
      m_LoadButton.SetActive(false);
      m_DeleteButton.SetActive(false);
    }
    else if (m_Selection.Count == 1)
    {
      m_versionInfoText.text = "<b>" + m_Selection[0].GetVersionName() + "</b>\r\n";
      m_versionInfoText.text += "<color=#C6C6C6>" + m_Selection[0].GetVersionTimeStamp() + "</color>";

      // Reenable buttons if they were gone before
      m_ExportButton.SetActive(true);
      m_LoadButton.SetActive(true);
      m_DeleteButton.SetActive(true);
    }
    else
    {
      m_versionInfoText.text = "<b>Multiple Version Selected</b>\r\n";
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

      // Remove the export and load buttons as we can't do that when multiple versions are selected
      m_ExportButton.SetActive(false);
      m_LoadButton.SetActive(false);
      m_DeleteButton.SetActive(true);
    }
  }

  // Removes any autosaves from the selection if their banched manual save is already selected
  private void DeselectTwiceSelectedAutosaves()
  {
    // m_Selection is sorted before this so we can check like this

    int lastManual = -1;
    for (int i = 0; i < m_Selection.Count; ++i)
    {
      if (m_Selection[i].IsManualSave())
      {
        lastManual = m_Selection[i].GetVersion().m_ManualVersion;
      }
      else if (lastManual == m_Selection[i].GetVersion().m_ManualVersion)
      {
        m_Selection[i].Deselect();
        m_Selection[i].SetColorAsSelected();
        m_Selection.RemoveAt(i);
        --i;
      }
    }
  }
}


// TODO: Filtering