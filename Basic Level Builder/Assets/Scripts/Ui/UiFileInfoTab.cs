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
  private TMPro.TextMeshProUGUI m_Description;

  private string m_FullFilePath;

  public override void InitLoad(string fullFilePath)
  {
    m_FullFilePath = fullFilePath;

    FileSystemInternal.FileInfo fileInfo;

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

    m_FileName.text = Path.GetFileNameWithoutExtension(fullFilePath);
    m_SaveNumber.text = fileInfo.m_FileData.m_ManualSaves.Count + " Manual Saves    " + fileInfo.m_FileData.m_AutoSaves.Count + " Auto Saves";
    string timeStamp = File.GetCreationTime(m_FullFilePath).ToString("M/d/yy h:mm:sstt").ToLower();
    m_CreationDate.text = $"<b>Created on:</b> <color=#C6C6C6>{timeStamp}</color>";

    FileSystemInternal.LevelData levelData = fileInfo.m_FileData.m_ManualSaves[^1];

    byte[] bytes = Convert.FromBase64String(levelData.m_Thumbnail);
    Texture2D tex = new(2, 2);
    tex.LoadImage(bytes);

    Sprite sprite = Sprite.Create(
            tex,
            new Rect(0, 0, tex.width, tex.height),
            new Vector2(0.5f, 0.5f) // pivot in the center
        );
    m_FileThumbnail.sprite = sprite;

    timeStamp = ((DateTime)levelData.m_TimeStamp).ToString("M/d/yy h:mm:sstt").ToLower();
    m_LatestVersion.text = $"<b>{levelData.m_Name}</b>\n<color=#C6C6C6>{timeStamp}</color>";
  }
}