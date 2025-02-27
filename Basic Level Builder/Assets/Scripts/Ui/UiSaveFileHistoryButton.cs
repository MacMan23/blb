using UnityEngine;

public class UiSaveFileHistoryButton : MonoBehaviour
{
  // Update is called once per frame
  void Update()
  {

  }

  public void OnClick()
  {
    // TODO: alot, make sure this isn't triggered when in other popups
    // Center to screen
    UiFileInfo infoBox = (UiFileInfo)Instantiate(Resources.Load("Ui/UiFileInfo"));
    UiSaveFileItem parent = GetComponentInParent<UiSaveFileItem>();
    if (parent != null)
      infoBox.InitLoad(parent.m_FullPath);
    else
      Debug.LogError("Could not find parent of history button");
  }
}
