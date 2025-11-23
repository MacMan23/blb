/***************************************************
Authors:        Brenden Epp
Last Updated:   11/22/2025

Copyright 2018-2025, DigiPen Institute of Technology
***************************************************/

using UnityEngine;
using System.IO;

public class ErrorLogger : MonoBehaviour
{
  private string logFilePath;

  void OnEnable()
  {
    // Define the log file path in a persistent location
    logFilePath = Path.Combine(Application.persistentDataPath, "ErrorLog.txt");

    // Subscribe to the log message event
    Application.logMessageReceived += HandleLog;
  }

  void OnDisable()
  {
    // Unsubscribe when the object is disabled or destroyed
    Application.logMessageReceived -= HandleLog;
  }

  void HandleLog(string logString, string stackTrace, LogType type)
  {
    // Only log errors and exceptions
    if (type == LogType.Error || type == LogType.Exception)
    {
      // Format the log entry
      string logEntry = $"[{System.DateTime.Now}] [{type}] {logString}\n{stackTrace}\n";

      // Append the log entry to the file
      File.AppendAllText(logFilePath, logEntry);
    }
  }
}