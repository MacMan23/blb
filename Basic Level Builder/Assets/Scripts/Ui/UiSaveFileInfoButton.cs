using UnityEngine;

public class UiSaveFileInfoButton : MonoBehaviour
{
  [SerializeField]
  private UiFileInfo m_FileInfoPrefab;
  // Update is called once per frame
  void Update()
  {

  }

  public void OnClick()
  {
    // TODO: alot, make sure this isn't triggered when in other popups
    // Center to screen
    // ModaldialougAdder?

    UiFileInfo infoBox = Instantiate(m_FileInfoPrefab);
    UiSaveFileItem parent = GetComponentInParent<UiSaveFileItem>();
    if (parent != null)
      infoBox.InitLoad(parent.m_FullPath);
    else
      Debug.LogError("Could not find parent of history button");
  }
}
