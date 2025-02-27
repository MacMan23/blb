using UnityEngine;

public class UiSaveFileInfoButton : MonoBehaviour
{
  [SerializeField]
  private UiFileInfo m_FileInfoPrefab;

  public void OnClick()
  {
    // TODO: alot, make sure this isn't triggered when in other popups
    // Center to screen

    GameObject root = GameObject.FindGameObjectWithTag("FileInfoRoot");
    if (!root)
    {
      Debug.LogError("Could not find FileInfoRoot");
      return;
    }

    UiFileInfo infoBox = Instantiate(m_FileInfoPrefab, root.GetComponent<RectTransform>());
    //RectTransform infoRect = infoBox.GetComponent<RectTransform>();
    //infoRect.SetAsFirstSibling();

    UiSaveFileItem parent = GetComponentInParent<UiSaveFileItem>();
    if (parent != null)
      infoBox.InitLoad(parent.m_FullPath);
    else
      Debug.LogError("Could not find parent of history button");
  }
}
