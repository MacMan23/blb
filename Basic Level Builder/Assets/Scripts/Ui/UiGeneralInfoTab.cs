/***************************************************
Authors:        Brenden Epp
Last Updated:   3/24/2025

Copyright 2018-2025, DigiPen Institute of Technology
***************************************************/

using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

public class UiGeneralInfoTab : UiTab
{
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

  private string m_FullFilePath;

  public override void InitLoad(string fullFilePath)
  {
    m_FullFilePath = fullFilePath;

    FileSystemInternal.FileInfo fileInfo;

    // Get data
    try
    {
      FileSystem.Instance.GetFileInfoFromFullFilePath(m_FullFilePath, out fileInfo);
    }
    catch (Exception e)
    {
      Debug.LogWarning($"Failed to get data from file path: {m_FullFilePath}. {e.Message}");
      StatusBar.Print($"Error: Could not load file history.");
      FindObjectOfType<UiFileInfo>().CloseWindow();
      return;
    }

    // Set text from file data
    m_FileNameTxt.text = Path.GetFileNameWithoutExtension(fullFilePath);
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

    byte[] bytes = Convert.FromBase64String(levelData.m_Thumbnail);
    Texture2D tex = new(0, 0); // No real reason for the width/height values in the constructor, they will be overwritten anyways in LoadImage
    tex.LoadImage(bytes);

    Sprite sprite = Sprite.Create(
            tex,
            new Rect(0, 0, tex.width, tex.height),
            new Vector2(0.5f, 0.5f) // pivot in the center
        );
    // Set the file thumbnail
    m_FileThumbnail.sprite = sprite;

    // Set text for the latest manial saves timestamp (to show where/when the thumbnail comes from)
    timeStamp = ((DateTime)levelData.m_TimeStamp).ToString("M/d/yy h:mm:sstt").ToLower();
    m_LatestVersionTxt.text = $"<b>{levelData.m_Name}</b>\n<color=#C6C6C6>{timeStamp}</color>";
  }

  public void SetFileDescription(string desc)
  {
    FileSystem.Instance.SetFileDescription(m_FullFilePath, desc);
  }
}