using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class AxisMappingEntry
{
  public string axisName;
  public List<string> buttons = new();
}

[CreateAssetMenu(fileName = "AxisMapping", menuName = "Input/Axis Mapping")]
public class AxisMapping : ScriptableObject
{
  public List<AxisMappingEntry> entries = new();

  private static AxisMapping _instance;

  public static AxisMapping Instance
  {
    get
    {
      if (_instance == null)
        _instance = Resources.Load<AxisMapping>("AxisMapping"); // looks for Resources/AxisMapping.asset
      return _instance;
    }
  }

  public IReadOnlyList<string> GetButtons(string axisName)
  {
    foreach (var entry in entries)
    {
      if (entry.axisName == axisName)
        return entry.buttons;
    }
    return new List<string>();
  }

  public string GetButtonsConcatenated(string axisName, string separator = ", ")
  {
    foreach (var entry in entries)
    {
      if (entry.axisName == axisName)
        return string.Join(separator, entry.buttons);
    }
    return string.Empty;
  }
}
