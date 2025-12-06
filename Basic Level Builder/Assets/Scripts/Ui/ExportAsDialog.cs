/***************************************************
Authors:        Brenden Epp
Last Updated:   5/6/2025

Copyright 2018-2025, DigiPen Institute of Technology
***************************************************/

public class ExportAsDialog : PromptFileNameDialog
{
  public override void Confirm()
  {
    if (!FileDirUtilities.IsFileNameValid(m_InputField.text))
      return;

    Close();
    FileSystem.Instance.TryStartExportSavingThread(m_InputField.text);
  }
}
