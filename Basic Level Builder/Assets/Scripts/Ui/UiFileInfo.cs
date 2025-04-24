/***************************************************
Authors:        Brenden Epp
Last Updated:   3/24/2025

Copyright 2018-2025, DigiPen Institute of Technology
***************************************************/

using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;

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

  private UiHistoryItem m_Selected = null;

  private string m_FullFilePath;

  void OnEnable()
  {
    UiHistoryItem.OnSelected += UpdateVersionInfo;
    UiHistoryItem.OnCloseInfoWindow += CloseWindow;
  }

  void OnDisable()
  {
    UiHistoryItem.OnSelected -= UpdateVersionInfo;
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
      catch (System.Exception e)
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
    catch (System.Exception e)
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
    if (m_Selected)
      m_Selected.Load();
  }

  // TODO: If deleting last manual save ask if want to delete whole file.
  // Or remove delete button if there is only one version left
  public void DeleteSelectedVersion()
  {
    // Remove all the autosave UiHistoryItems from the scene
    if (m_Selected.IsManualSave())
    {
      DeleteMaunalsAutosaves(m_Selected.GetVersion());
    }

    // Delete the manaul version, which also deletes the autosaves from the data
    m_Selected.DeleteVersion();

    // We changed up a lot deleting the manual and its autos
    // So delete and recreate the item list
    if (m_Selected.IsManualSave())
    {
      ClearHistoryItemList();
      LoadHistoryItemList();
    }
    else
    {
      UpdateVersionList();
      Deselect();
    }
  }

  private void DeleteMaunalsAutosaves(int version)
  {
    List<UiHistoryItem> items = GetAllHistoryItems();
    foreach (var item in items)
    {
      if (!item.IsManualSave() && item.GetBranchVersion() == version)
      {
        DestroyImmediate(item.gameObject);
      }
    }
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
    UpdateVersionInfo(null);
  }

  private void UpdateVersionInfo(UiHistoryItem selectedItem)
  {
    // Clear text if null
    if (selectedItem == null)
    {
      m_Selected = null;
      m_versionInfoText.text = "";
      return;
    }

    m_Selected = selectedItem;

    m_versionInfoText.text = "<b>" + selectedItem.GetVersionName() + "</b>\r\n";
    m_versionInfoText.text += "<color=#C6C6C6>" + selectedItem.GetVersionTimeStamp() + "</color>";
  }
}


// TODO: Filtering