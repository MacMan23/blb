using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class UiListView : MonoBehaviour
{
  public Color m_EvenColor = new(0.3254902f, 0.3254902f, 0.3254902f);
  public Color m_OddColor = new(0.2901961f, 0.2901961f, 0.2901961f);

  public ColorBlock m_ButtonColors;

  private ColorBlock m_EvenBlock;
  private ColorBlock m_OddBlock;

  RectTransform m_RectTransform;


  void Awake()
  {
    m_RectTransform = GetComponent<RectTransform>();

    m_EvenBlock = m_OddBlock = m_ButtonColors;

    m_EvenBlock.normalColor = m_EvenColor;
    m_OddBlock.normalColor = m_OddColor;
  }


  public void Add(RectTransform item)
  {
    AddHelper(item);
    AssignColors();
  }


  public void Add(List<RectTransform> items)
  {
    foreach (var item in items)
      AddHelper(item);
    AssignColors();
  }


  public void Remove(RectTransform item)
  {
    RemoveHelper(item);
    AssignColors();
  }


  public void Remove(List<RectTransform> items)
  {
    foreach (var item in items)
      RemoveHelper(item);
    AssignColors();
  }


  public void Clear()
  {
    DestroyAll();
  }


  void RemoveHelper(RectTransform item)
  {
    Destroy(item.gameObject);
  }


  void AddHelper(RectTransform item)
  {
    item.SetParent(m_RectTransform);
    item.SetAsFirstSibling();
  }


  void DestroyAll()
  {
    foreach (Transform item in m_RectTransform)
      Destroy(item.gameObject);
  }


  void AssignColors()
  {
    var files = m_RectTransform.GetComponentsInChildren<UiSaveFileItem>();
    var odd = false;

    foreach (var file in files)
    {
      if (!file.TryGetComponent(out Button button))
        return;
      button.colors = odd ? m_OddBlock : m_EvenBlock;
      odd = !odd;
    }
  }


  public UiSaveFileItem GetOldestItem()
  {
    var items = m_RectTransform.GetComponentsInChildren<UiSaveFileItem>();
    var returnIndex = items.Length - 1;

    return returnIndex >= 0 ? items[items.Length - 1] : null;
  }


  public UiSaveFileItem GetItemByFullPath(string fullPath)
  {
    var items = m_RectTransform.GetComponentsInChildren<UiSaveFileItem>();

    foreach (var item in items)
    {
      if (item.m_FullPath == fullPath)
        return item;
    }

    return null;
  }

  // Will check if each file exists and removes the ui if the file is missing
  public void ValidateAllItems()
  {
    var items = m_RectTransform.GetComponentsInChildren<UiSaveFileItem>();
    List<RectTransform> removeItems = new();
    foreach (var item in items)
    {
      if (!File.Exists(item.m_FullPath))
        removeItems.Add(item.GetComponent<RectTransform>());
    }

    Remove(removeItems);
  }

  public void MoveToTop(Transform item)
  {
    if (item.parent != m_RectTransform)
      return;

    item.SetAsFirstSibling();
    AssignColors();
  }
}
