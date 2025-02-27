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

    List< UiHistoryItem> items = new();

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
    }
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
}
