/***************************************************
Authors:        Douglas Zwick
Last Updated:   ???

Copyright 2018-2025, DigiPen Institute of Technology
***************************************************/


using UnityEngine;

public class HotkeyMaster : MonoBehaviour
{
  public static bool s_HotkeysEnabled = true;

  public static bool IsMultiSelectHeld()
  {
    if (Application.platform == RuntimePlatform.OSXPlayer)
      return Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);

    return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
  }

  public static bool IsRangeSelectHeld()
  {
    return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
  }

  public static bool IsPrimaryModifierHeld()
  {
    var shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

    if (Application.isEditor || Application.platform == RuntimePlatform.WebGLPlayer)
      return shiftHeld;
    if (Application.platform == RuntimePlatform.OSXPlayer)
      return Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand) || shiftHeld;

    return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || shiftHeld;
  }


  public static bool IsSecondaryModifierHeld()
  {
    return Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
  }


  public static bool IsPairedModifierHeld()
  {
    var standardSecondaryHeld = IsSecondaryModifierHeld();
    var shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

    if (Application.isEditor || Application.platform == RuntimePlatform.WebGLPlayer)
      return shiftHeld && standardSecondaryHeld;
    if (Application.platform == RuntimePlatform.OSXPlayer)
      return (Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand)) &&
        (standardSecondaryHeld || shiftHeld);

    return (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
      (standardSecondaryHeld || shiftHeld);
  }
}
