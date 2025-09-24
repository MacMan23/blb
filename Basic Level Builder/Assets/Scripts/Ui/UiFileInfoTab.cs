/***************************************************
Authors:        Brenden Epp
Last Updated:   3/24/2025

Copyright 2018-2025, DigiPen Institute of Technology
***************************************************/

using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

public class UiFileInfoTab : UiFileTab
{
  [Header("Visual Components")]
  [SerializeField]
  private TMPro.TextMeshProUGUI m_FileName;
  [SerializeField]
  private TMPro.TextMeshProUGUI m_SaveNumber;
  [SerializeField]
  private TMPro.TextMeshProUGUI m_CreationDate;
  [SerializeField]
  private Image m_FileThumbnail;
  [SerializeField]
  private TMPro.TextMeshProUGUI m_LatestVersion;
  [SerializeField]
  private TMPro.TMP_InputField m_Description;

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
    m_FileName.text = Path.GetFileNameWithoutExtension(fullFilePath);
    m_SaveNumber.text = fileInfo.m_FileData.m_ManualSaves.Count + " Manual Saves    " + fileInfo.m_FileData.m_AutoSaves.Count + " Auto Saves";
    string timeStamp = File.GetCreationTime(m_FullFilePath).ToString("M/d/yy h:mm:sstt").ToLower();
    m_CreationDate.text = $"<b>Created on:</b> <color=#C6C6C6>{timeStamp}</color>";

    // Set the text description for the file
    m_Description.text = fileInfo.m_FileData.m_Description;
    m_Description.ForceLabelUpdate();


    // Get latest manual save and its thumbnail
    FileSystemInternal.LevelData levelData = fileInfo.m_FileData.m_ManualSaves[^1];
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
    m_LatestVersion.text = $"<b>{levelData.m_Name}</b>\n<color=#C6C6C6>{timeStamp}</color>";
  }

  public void SetFileDescription(string desc)
  {
    FileSystem.Instance.SetFileDescription(m_FullFilePath, desc);
  }
}