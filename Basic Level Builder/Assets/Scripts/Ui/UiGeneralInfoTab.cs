/***************************************************
Authors:        Brenden Epp
Last Updated:   3/24/2025

Copyright 2018-2025, DigiPen Institute of Technology
***************************************************/

using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UiGeneralInfoTab : UiTab
{
  [SerializeField]
  private UiFileInfo m_FileInfo;

  [Header("Visual Components")]
  [SerializeField]
  private TMPro.TextMeshProUGUI m_FileNameTxt;
  [SerializeField]
  private TMPro.TextMeshProUGUI m_SaveNumberTxt;
  [SerializeField]
  private TMPro.TextMeshProUGUI m_CreationDateTxt;
  [SerializeField]
  private Image m_FileThumbnail;
  [SerializeField]
  private TMPro.TextMeshProUGUI m_LatestVersionTxt;
  [SerializeField]
  private TMPro.TMP_InputField m_DescriptionInputField;
  [SerializeField]
  private TMPro.TMP_InputField m_FileNameInput;

  private string m_FullFilePath;

  public static char[] s_InvalidFileNameChars = Path.GetInvalidFileNameChars();
  public static string s_InvalidCharsString;
  protected string m_CurrentValidName = string.Empty;

  private void Awake()
  {
    var printableInvalidChars = s_InvalidFileNameChars.Where(invalidChar =>
      !char.IsControl(invalidChar)).ToArray();
    s_InvalidCharsString = string.Join(" ", printableInvalidChars);
  }

  public override void InitLoad(string fullFilePath)
  {
    m_FullFilePath = fullFilePath;

    // Get data
    if (!ReadFile(out FileSystemInternal.FileInfo fileInfo))
      return;

    // Set text from file data
    m_FileNameTxt.text = Path.GetFileNameWithoutExtension(fullFilePath);
    m_FileNameInput.text = m_FileNameTxt.text;

    UpdateLatestVersionPreview(fileInfo);
  }

  public override void OpenTab()
  {
    if (!ReadFile(out FileSystemInternal.FileInfo fileInfo))
      return;
    UpdateLatestVersionPreview(fileInfo);
  }

  // Returns false if an error occurec
  private bool ReadFile(out FileSystemInternal.FileInfo fileInfo)
  {
    try
    {
      FileSystem.Instance.GetFileInfoFromFullFilePath(m_FullFilePath, out fileInfo);
    }
    catch (Exception e)
    {
      Debug.LogWarning($"Failed to get data from file path: {m_FullFilePath}. {e.Message}");
      StatusBar.Print($"Error: Could not load file history.");
      FindObjectOfType<UiFileInfo>().CloseWindow();
      fileInfo = new();
      return false;
    }
    return true;
  }

  private void UpdateLatestVersionPreview(FileSystemInternal.FileInfo fileInfo)
  {
    m_SaveNumberTxt.text = fileInfo.m_FileData.m_ManualSaves.Count + " Manual Saves    " + fileInfo.m_FileData.m_AutoSaves.Count + " Auto Saves";
    string timeStamp = File.GetCreationTime(m_FullFilePath).ToString("M/d/yy h:mm:sstt").ToLower();
    m_CreationDateTxt.text = $"<b>Created on:</b> <color=#C6C6C6>{timeStamp}</color>";
    
    // Set the text description for the file
    m_DescriptionInputField.text = fileInfo.m_FileData.m_Description;
    m_DescriptionInputField.ForceLabelUpdate();

    // Get latest manual save and its thumbnail
    FileSystemInternal.LevelData levelData;
    // Check if we have any manual saves first
    if (fileInfo.m_FileData.m_ManualSaves.Count > 0)
      levelData = fileInfo.m_FileData.m_ManualSaves[^1];
    else if (fileInfo.m_FileData.m_AutoSaves.Count > 0)
      levelData = fileInfo.m_FileData.m_AutoSaves[^1];
    else
    {
      Debug.LogWarning($"No saves found in file \"{m_FullFilePath}\"");
      StatusBar.Print($"Error: Could not load file history. No saves found in file \"{m_FullFilePath}\"");
      FindObjectOfType<UiFileInfo>().CloseWindow();
      return;
    }

    m_FileThumbnail.sprite = FileVersioning.GetThumbnailSprite(levelData);

    // Set text for the latest manial saves timestamp (to show where/when the thumbnail comes from)
    string latestVersionName = levelData.m_Name;

    if (string.IsNullOrEmpty(latestVersionName))
    {
      latestVersionName = "Version " + levelData.m_Version.m_ManualVersion;
    }

    timeStamp = ((DateTime)levelData.m_TimeStamp).ToString("M/d/yy h:mm:sstt").ToLower();
    m_LatestVersionTxt.text = $"<b>{latestVersionName}</b>{Environment.NewLine}<color=#C6C6C6>{timeStamp}</color>";
  }

  public void SetFileDescription(string desc)
  {
    FileSystem.Instance.SetFileDescription(m_FullFilePath, desc);
  }

  // Name input field functions

  public void OnInputFieldDeselect()
  {
    // Move text position back to the left
    m_FileNameInput.textComponent.rectTransform.localPosition = Vector3.zero;
    m_FileNameInput.caretPosition = 0;
    // Re-disable mouse event blocking
    m_FileNameInput.GetComponentInChildren<TMPro.TMP_SelectionCaret>(true).raycastTarget = false;

    m_FileNameInput.textComponent.overflowMode = TMPro.TextOverflowModes.Ellipsis;
    m_FileNameTxt.overflowMode = TMPro.TextOverflowModes.Ellipsis;
  }

  public void OnInputFieldSelect()
  {
    m_FileNameInput.textComponent.overflowMode = TMPro.TextOverflowModes.Masking;
    m_FileNameTxt.overflowMode = TMPro.TextOverflowModes.Masking;
  }

  public void EditName()
  {
    m_FileNameInput.GetComponentInChildren<TMPro.TMP_SelectionCaret>(true).raycastTarget = true;
    m_FileNameInput.ActivateInputField();
  }

  public void OnInputFieldValueChanged(string value)
  {
    var invalidChars = s_InvalidFileNameChars;

    if (value.IndexOfAny(invalidChars) >= 0)
    {
      var message = $"<color=#ffff00>The string <b>{value}</b> contains " +
        $"one or more invalid characters: <b>{s_InvalidCharsString}</b></color>";
      StatusBar.Print(message);

      m_FileNameInput.text = m_CurrentValidName;
    }
    else
    {
      m_CurrentValidName = value;
    }

    m_FileNameTxt.text = m_CurrentValidName + ".";
  }

  public void SetName()
  {
    if (!IsValidName())
    {
      m_FileNameInput.text = Path.GetFileNameWithoutExtension(m_FullFilePath);
      return;
    }

    // Set name
    m_FullFilePath = FileSystem.Instance.RenameFile(m_FullFilePath, m_CurrentValidName);
    m_FileInfo.SetTitleBarText(m_CurrentValidName);

    // Deletect all ui so that the input field will be deselected and update its text
    var eventSystem = EventSystem.current;
    if (!eventSystem.alreadySelecting) eventSystem.SetSelectedGameObject(null);
  }

  protected bool IsValidName()
  {
    var emptyName = m_CurrentValidName == string.Empty;
    var whiteSpaceName = string.IsNullOrWhiteSpace(m_CurrentValidName);

    if (emptyName || whiteSpaceName)
    {
      StatusBar.Print("<color=#ffff00>Entered file name is invalid.</color>");

      return false;
    }
    return true;
  }
}