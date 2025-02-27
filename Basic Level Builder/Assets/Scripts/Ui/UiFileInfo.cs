using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static UnityEditor.Progress;

public class UiFileInfo : MonoBehaviour
{
  private string m_fileFullPath;
  [SerializeField]
  private UiHistoryItem m_ManualSaveItem;
  [SerializeField]
  private UiHistoryItem m_AutoSaveItem;
  [SerializeField]
  private RectTransform m_Content;

  // Start is called before the first frame update
  void Start()
  {

  }

  // Update is called once per frame
  void Update()
  {

  }

  public void InitLoad(string fullFilePath)
  {
    m_fileFullPath = fullFilePath;

    // Create all the file items
    // Load the files data
    FileSystem.Instance.GetDataFromFullPath(fullFilePath, out FileSystem.FileData filedata, out FileSystem.Header _header);
    foreach (var levelData in filedata.m_ManualSaves)
    {
      UiHistoryItem historyItem = Instantiate(m_ManualSaveItem);
      // Give level data so it can init its text and thumbnail
      historyItem.Init(levelData);
      // Add item to list view
      if (historyItem.TryGetComponent(out RectTransform ret))
      {
        ret.SetParent(m_Content);
        // IDK copied from UiListView
        ret.SetAsFirstSibling();
      }
    }
  }
}
