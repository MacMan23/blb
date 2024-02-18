using System.Collections.Generic;
using UnityEngine;

public class ModalDialogAdder : MonoBehaviour
{
  public List<ModalDialog> m_ModalDialogPrefabs;

  ModalDialogMaster m_ModalDialogMaster;


  private void Awake()
  {
    m_ModalDialogMaster = FindObjectOfType<ModalDialogMaster>();
  }

  #region Dialogs request
  public void RequestDialogsAtTransform()
  {
    RequestDialogsAtTransformWithStrings();
  }


  public void RequestDialogsAtCenter()
  {
    RequestDialogsAtCenterWithStrings();
  }

  public void RequestDialogsAtTransformWithStrings(params string[] strings)
  {
    if (GlobalData.AreEffectsUnderway())
      return;

    var worldPoint = transform.position;

    foreach (var prefab in m_ModalDialogPrefabs)
      m_ModalDialogMaster.RequestDialogAtWorldPoint(prefab, worldPoint, strings);

    m_ModalDialogMaster.Begin(true);
  }

  public void RequestDialogsAtCenterWithStrings(params string[] strings)
  {
    if (GlobalData.AreEffectsUnderway())
      return;

    foreach (var prefab in m_ModalDialogPrefabs)
      m_ModalDialogMaster.RequestDialogAtCenter(prefab, strings);

    m_ModalDialogMaster.Begin(false);
  }
  #endregion

  #region Dialog request
  public void RequestDialogAtTransform(int dialogInex = 0)
  {
    RequestDialogAtTransformWithStrings(dialogInex);
  }


  public void RequestDialogAtCenter(int dialogInex = 0)
  {
    RequestDialogAtCenterWithStrings(dialogInex);
  }

  public void RequestDialogAtTransformWithStrings(int dialogInex = 0, params string[] strings)
  {
    if (GlobalData.AreEffectsUnderway())
      return;

    var worldPoint = transform.position;

    m_ModalDialogMaster.RequestDialogAtWorldPoint(m_ModalDialogPrefabs[dialogInex], worldPoint, strings);

    m_ModalDialogMaster.Begin(true);
  }


  public void RequestDialogAtCenterWithStrings(int dialogInex = 0, params string[] strings)
  {
    if (GlobalData.AreEffectsUnderway())
      return;

    m_ModalDialogMaster.RequestDialogAtCenter(m_ModalDialogPrefabs[dialogInex], strings);

    m_ModalDialogMaster.Begin(false);
  }
  #endregion
}
