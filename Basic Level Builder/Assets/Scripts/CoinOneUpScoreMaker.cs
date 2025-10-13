using UnityEngine;

public class CoinOneUpScoreMaker : MonoBehaviour
{
  public GameObject m_OneUpScorePrefab;


  public void OnOneUpThresholdReached(CoinCollectorEventData eventData)
  {
    var coinPosition = eventData.m_Coin.transform.position;
    Instantiate(m_OneUpScorePrefab, coinPosition, Quaternion.identity);
  }
}
