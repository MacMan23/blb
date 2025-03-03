using UnityEngine;
using static FileSystem;
using System.Collections.Generic;

public class UiFileInfo : MonoBehaviour
{
  private string m_fileFullPath;
  [SerializeField]
  private UiHistoryItem m_ManualSaveItemPrefab;
  [SerializeField]
  private UiHistoryItem m_AutoSaveItemPrefab;
  [SerializeField]
  private RectTransform m_Content;

  public void InitLoad(string fullFilePath)
  {
    m_fileFullPath = fullFilePath;

    // Create all the file items
    // Load the files data
    Instance.GetDataFromFullPath(fullFilePath, out FileData filedata, out Header _header);

    List<UiHistoryItem> items = new();

    foreach (var levelData in filedata.m_ManualSaves)
    {
      items.Add(CreateHistoryItem(levelData, m_ManualSaveItemPrefab));
    }
    foreach (var levelData in filedata.m_AutoSaves)
    {
      items.Add(CreateHistoryItem(levelData, m_AutoSaveItemPrefab));
    }

    // Properly sort items
    items.Sort((a, b) => a.CompareTo(b));
    for (int i = 0; i < items.Count; i++)
    {
      items[i].transform.SetSiblingIndex(i);

      // Set the prev item as the last auto save in the list so the branch lines don'e continue
      if (i != 0 && items[i].IsManualSave())
      {
        items[i - 1].SetLastAutoSave();
      }
      // Or if this is the last item and auto, then do the same for this item
      else if (i == items.Count - 1 && !items[i].IsManualSave())
      {
        items[i].SetLastAutoSave();
      }
    }

    items[0].Select();
    // First time run will collapse all
    ToggleSaveExpansion();
  }

  private UiHistoryItem CreateHistoryItem(LevelData levelData, UiHistoryItem prefab)
  {
    UiHistoryItem historyItem = Instantiate(prefab);
    // Give level data so it can init its text and thumbnail
    historyItem.Init(levelData);
    // Add item to list view
    if (historyItem.TryGetComponent(out RectTransform rect))
    {
      rect.SetParent(m_Content);
      // IDK copied from UiListView
      rect.SetAsFirstSibling();
    }
    return historyItem;
  }

  public void ToggleSaveExpansion()
  {
    bool expanding = GetNumberExpandedSaves() == 0;

    for (int i = 0; i < m_Content.childCount; i++)
    {
      var child = m_Content.GetChild(i);
      if (child != null && child.TryGetComponent<UiHistoryItem>(out var item) &&
          item.IsManualSave() && item.IsExpanded() == !expanding)
      {
        item.ToggleExpand();
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
}


// TODO: Filtering