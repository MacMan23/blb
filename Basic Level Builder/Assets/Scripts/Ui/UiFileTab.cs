using UnityEngine;

public abstract class UiFileTab : MonoBehaviour
{
  public GameObject m_TabButton;

  public abstract void InitLoad(string fullFilePath);
}
