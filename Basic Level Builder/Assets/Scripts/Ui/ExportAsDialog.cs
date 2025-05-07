/***************************************************
Authors:        Brenden Epp
Last Updated:   5/6/2025

Copyright 2018-2025, DigiPen Institute of Technology
***************************************************/

public class ExportAsDialog : PromptFileNameDialog
{
  public override void Confirm()
  {
    if (!IsValidName())
      return;

    Close();
    FileSystem.Instance.StartExportSavingThread(m_CurrentValidName);
  }
}
