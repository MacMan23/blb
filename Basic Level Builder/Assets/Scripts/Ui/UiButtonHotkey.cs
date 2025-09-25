using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class UiButtonHotkey : MonoBehaviour
{
  [System.Serializable]
  public class Hotkey
  {
    public string m_Button;
    public ModifierRequirements m_Modifiers;
    public UnityEvent m_Event;
    public bool m_SubjectToHotkeyDisabling;
    public bool m_AllowedInPlayMode;
    public bool m_AllowedInUiPopups = false;
  }

  [System.Serializable]
  public enum ModifierRequirements
  {
    None,
    Primary,
    Secondary,
    Paired,
  }

  public List<Hotkey> m_List = new();

  public string GetHotkeyString()
  {
    string buttonKeys = AxisMapping.Instance.GetButtonsConcatenated(m_List[0].m_Button);
    string mod = ModifierToString(m_List[0].m_Modifiers);
    if (string.IsNullOrEmpty(mod))
      return $"({buttonKeys})";
    else
      return $"({mod} + {buttonKeys})";
  }

  private string ModifierToString(ModifierRequirements mod)
  {
    switch (mod)
    {
      case ModifierRequirements.Primary:
        return GetPrimaryModifier();
      case ModifierRequirements.Secondary:
        return "Shift";
      case ModifierRequirements.Paired:
        return GetPrimaryModifier() + " + Shift";
      default:
        return string.Empty;
    }
  }

  private string GetPrimaryModifier()
  {
    if (Application.isEditor || Application.platform == RuntimePlatform.WebGLPlayer)
      return "Alt";
    if (Application.platform is RuntimePlatform.OSXPlayer or RuntimePlatform.OSXEditor)
      return "Cmd";
    return "Ctrl";
  }

  void Update()
  {
    foreach (var hotkey in m_List)
    {
      if (hotkey.m_SubjectToHotkeyDisabling && !HotkeyMaster.s_HotkeysEnabled)
        return;

      if (!hotkey.m_AllowedInPlayMode && GlobalData.IsInPlayMode())
        return;

      if (!hotkey.m_AllowedInUiPopups && GlobalData.IsInUiPopup())
        return;

      switch (hotkey.m_Modifiers)
      {
        case ModifierRequirements.Primary:
          if (HotkeyMaster.IsPrimaryModifierHeld())
            goto default;
          break;
        case ModifierRequirements.Secondary:
          if (HotkeyMaster.IsSecondaryModifierHeld())
            goto default;
          break;
        case ModifierRequirements.Paired:
          if (HotkeyMaster.IsPairedModifierHeld())
            goto default;
          break;
        default:  // ModifierRequirements.None
          if (Input.GetButtonDown(hotkey.m_Button))
            hotkey.m_Event.Invoke();
          break;
      }
    }
  }
}
