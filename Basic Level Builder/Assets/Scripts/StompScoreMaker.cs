using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StompScoreMaker : MonoBehaviour
{
  public List<GameObject> m_ScorePrefabs;


  public void OnStompedEnemy(HealthEventData eventData)
  {
    var prefabIndex = Math.Min(eventData.m_StompComboCounter,
      m_ScorePrefabs.Count - 1);
    var prefab = m_ScorePrefabs[prefabIndex];
    Instantiate(prefab, eventData.m_EnemyPosition, Quaternion.identity);
  }
}
