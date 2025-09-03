/***************************************************
Authors:        Brenden Epp
Last Updated:   3/24/2025

Copyright 2018-2025, DigiPen Institute of Technology
***************************************************/

using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

public class UiFileInfo : MonoBehaviour
{
  [SerializeField]
  private TMPro.TextMeshProUGUI m_TitlebarText;

  [SerializeField]
  private Vector2 m_MinimizedTabSizeDelta;
  [SerializeField]
  private Vector2 m_MaximizedTabSizeDelta;
  [SerializeField]
  private Color m_MinimizedColor;
  [SerializeField]
  private Color m_MaximizedColor;

  private List<UiFileTab> m_Tabs = new();

  private string m_FullFilePath;

  void Awake()
  {
    // Adds all the tab bodys to the tab list
    foreach (var tab in GetComponentsInChildren<UiFileTab>(true))
      m_Tabs.Add(tab);
  }

  void Start()
  {
    if (m_Tabs.Count > 0)
      OpenTab(m_Tabs[0]);
  }


  void OnEnable()
  {
    UiHistoryItem.OnCloseInfoWindow += CloseWindow;
  }

  void OnDisable()
  {
    UiHistoryItem.OnCloseInfoWindow -= CloseWindow;
  }

  public void InitLoad(string fullFilePath)
  {
    m_FullFilePath = fullFilePath;
    m_TitlebarText.text = Path.GetFileNameWithoutExtension(fullFilePath);

    foreach (var tab in m_Tabs)
      tab.InitLoad(fullFilePath);
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
    GlobalData.DecrementUiPopup();

    Destroy(gameObject);
  }

  public void OpenTab(UiFileTab tabRef)
  {
    foreach (var tab in m_Tabs)
    {
      tab.gameObject.SetActive(tab == tabRef);

      if (tab == tabRef)
      {
        //(tab.m_TabButton.transform as RectTransform).localScale = m_MaximizedTab.localScale;
        (tab.m_TabButton.transform as RectTransform).sizeDelta = m_MaximizedTabSizeDelta;

        tab.m_TabButton.GetComponent<Image>().color = m_MaximizedColor;
      }
      else
      {
        //(tab.m_TabButton.transform as RectTransform).localScale = m_MinimizedTab.localScale;
        (tab.m_TabButton.transform as RectTransform).sizeDelta = m_MinimizedTabSizeDelta;

        tab.m_TabButton.GetComponent<Image>().color = m_MinimizedColor;
      }
    }
  }
}