/***************************************************
Authors:        System
Last Updated:   3/21/2026

Description:
  Generic object pooling system for reusing GameObjects,
  specifically optimized for SOLID tile instantiation.

Copyright 2018-2025, DigiPen Institute of Technology
***************************************************/

using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public class ObjectPool
{
  private Stack<GameObject> m_AvailableObjects = new();
  private HashSet<GameObject> m_AllPooledObjects = new();
  private GameObject m_Prefab;
  private Transform m_PoolParent;
  private int m_MaxPoolSize;

  public ObjectPool(GameObject prefab, int initialSize, int maxSize, Transform poolParent = null)
  {
    m_Prefab = prefab;
    m_MaxPoolSize = maxSize;
    m_PoolParent = poolParent;
    PreInstantiate(initialSize);
  }

  public void PreInstantiate(int count)
  {
    for (int i = 0; i < count; ++i)
    {
      var obj = Object.Instantiate(m_Prefab, m_PoolParent);
      obj.SetActive(false);
      m_AvailableObjects.Push(obj);
      m_AllPooledObjects.Add(obj);
    }
  }

  public GameObject GetObject()
  {
    GameObject obj;
    if (m_AvailableObjects.Count > 0)
    {
      obj = m_AvailableObjects.Pop();
    }
    else
    {
      obj = Object.Instantiate(m_Prefab, m_PoolParent);
      m_AllPooledObjects.Add(obj);
    }
    obj.SetActive(true);
    return obj;
  }

  public void ReturnToPool(GameObject obj)
  {
    if (!m_AllPooledObjects.Contains(obj))
      return;
    if (m_AvailableObjects.Count >= m_MaxPoolSize)
    {
      Object.Destroy(obj);
      m_AllPooledObjects.Remove(obj);
      return;
    }
    
    // Clean up dynamic components to avoid state carryover
    // Remove PathMover if present
    var pathMover = obj.GetComponent<PathMover>();
    if (pathMover != null)
      Object.Destroy(pathMover);
    
    // Remove ContactParent if present
    var contactParent = obj.GetComponent<ContactParent>();
    if (contactParent != null)
      Object.Destroy(contactParent);
    
    // Reset Rigidbody2D if present
    if (obj.TryGetComponent<Rigidbody2D>(out var rigidbody))
    {
      rigidbody.velocity = Vector2.zero;
      rigidbody.angularVelocity = 0f;
      rigidbody.isKinematic = false;
    }
    
    // Reset SolidEdgeOutliner state if present
    if (obj.TryGetComponent<SolidEdgeOutliner>(out var solidEdgeOutliner))
    {
      // Reset the m_BeingErased flag using reflection (private field)
      var fieldInfo = typeof(SolidEdgeOutliner).GetField("m_BeingErased", 
        BindingFlags.NonPublic | BindingFlags.Instance);
      if (fieldInfo != null)
        fieldInfo.SetValue(solidEdgeOutliner, false);
    }
    
    // Reset transform and move to pool parent location
    obj.transform.SetParent(m_PoolParent);
    obj.transform.position = Vector3.zero;
    obj.transform.rotation = Quaternion.identity;
    obj.transform.localScale = Vector3.one;
    
    obj.SetActive(false);
    m_AvailableObjects.Push(obj);
  }

  public void ClearPool()
  {
    foreach (var obj in m_AllPooledObjects)
    {
      Object.Destroy(obj);
    }
    m_AllPooledObjects.Clear();
    m_AvailableObjects.Clear();
  }

  public int GetPoolSize()
  {
    return m_AvailableObjects.Count;
  }

  public int GetTotalPooledCount()
  {
    return m_AllPooledObjects.Count;
  }
}
