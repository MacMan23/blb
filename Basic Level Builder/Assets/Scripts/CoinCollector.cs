using UnityEngine;
using UnityEngine.Events;

public class CoinCollector : MonoBehaviour
{
  [System.Serializable]
  public class Events
  {
    public CoinCollectorEvent Collected;
    public CoinCollectorEvent OneUpThresholdReached;
  }

  public Events m_Events;

  /// <summary>
  /// Collect this many coins to earn a 1up!
  /// </summary>
  public int m_OneUpThreshold = 100;

  private int m_Total = 0;
  private int m_LastModulo = 0;


  private void OnCollisionEnter2D(Collision2D collision)
  {
    Collision(collision.gameObject);
  }


  private void OnTriggerEnter2D(Collider2D collision)
  {
    Collision(collision.gameObject);
  }


  void Collision(GameObject obj)
  {
    if (!enabled)
      return;

    var coin = obj.GetComponent<Coin>();

    if (coin == null)
      return;

    coin.AttemptCollect(this);
  }


  public void Collected(Coin coin)
  {
    if (!enabled)
      return;

    var value = coin.m_Value;
    m_Total += value;

    GlobalData.DispatchCoinCollected(m_Total);

    var eventData = new CoinCollectorEventData()
    {
      m_Coin = coin,
      m_Value = value,
    };

    m_Events.Collected.Invoke(eventData);

    var currentModulo = m_Total % m_OneUpThreshold;

    if (value > 0 && currentModulo <= m_LastModulo)
      m_Events.OneUpThresholdReached.Invoke(eventData);

    m_LastModulo = currentModulo;
  }


  public void OnDied(HealthEventData eventData)
  {
    enabled = false;
  }


  public void OnReturned(HealthEventData eventData)
  {
    enabled = true;
  }
}


[System.Serializable]
public class CoinCollectorEvent : UnityEvent<CoinCollectorEventData> { }

public class CoinCollectorEventData
{
  public Coin m_Coin;
  public int m_Value;
  public int m_Total;
}
