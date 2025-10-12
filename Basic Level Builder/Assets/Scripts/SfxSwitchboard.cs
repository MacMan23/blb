using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SfxSwitchboard : MonoBehaviour
{
  public List<SfxPlayer> m_Players = new();


  public void OnDeath()
  {
    foreach (var player in m_Players)
      player.OnDeath();
  }
  

  public void OnReturned()
  {
    foreach (var player in m_Players)
      player.OnReturned();
  }
}
