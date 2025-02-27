using UnityEngine;
using static FileSystem;

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
    foreach (var levelData in filedata.m_ManualSaves)
    {
      CreateHistoryItem(levelData, m_ManualSaveItemPrefab);
    }
    foreach (var levelData in filedata.m_AutoSaves)
    {
      CreateHistoryItem(levelData, m_AutoSaveItemPrefab);
    }
  }

  private void CreateHistoryItem(LevelData levelData, UiHistoryItem prefab)
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
  }
}
