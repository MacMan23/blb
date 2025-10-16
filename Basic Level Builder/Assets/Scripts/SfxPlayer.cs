using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class SfxPlayer : MonoBehaviour
{
  public List<AudioClip> m_Sfx = new();
  public float m_Cooldown = 0;
  public bool m_DisableOnDeath = true;
  public bool m_InterruptSelf = false;

  private AudioSource m_AudioSource;
  private bool m_Dead = false;
  private float m_CooldownTimer = 0;

  private bool CoolingDown { get { return m_CooldownTimer > 0; }}


  private void Awake()
  {
    m_AudioSource = GetComponent<AudioSource>();
  }


  void Update()
  {
    if (!CoolingDown) return;

    m_CooldownTimer -= Time.deltaTime;
  }


  public void AttemptPlay()
  {
    AttemptPlay(null);
  }


  public void AttemptPlay(AudioClip clip)
  {
    if (CoolingDown) return;

    Play(clip);
  }


  private void Play(AudioClip clip)
  {
    if (m_DisableOnDeath && m_Dead) return;

    if (clip == null)
    {
      clip = SelectRandomClip();
      if (clip == null) return;
    }

    if (m_InterruptSelf && m_AudioSource.isPlaying)
      m_AudioSource.Stop();

    m_AudioSource.PlayOneShot(clip);

    m_CooldownTimer = m_Cooldown;
  }


  public void OnDeath()
  {
    m_Dead = true;
  }
  

  public void OnReturned()
  {
    m_Dead = false;
  }


  private AudioClip SelectRandomClip()
  {
    return m_Sfx.Count <= 0 ? null : m_Sfx[Random.Range(0, m_Sfx.Count - 1)];
  }
}
