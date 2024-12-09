using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class UiSaveFileItem : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
	public TextMeshProUGUI m_Text;
	public TextMeshProUGUI m_TimeStamp;
	public GameObject m_InfoButton;
	public Color m_AutosaveColor = Color.white;
	public Color m_ManualSaveColor = Color.yellow;

	public float m_HiddenRectTransformWidth = 2.0f;
	public float m_VisibleRectTransformWidth = 31.0f;

	FileSystem m_FileSystem;
	public string m_FullPath { get; private set; }

	private bool m_IsMouseHovering = false;


	public void Setup(FileSystem fileSystem, string fullPath, string fileName)
	{
		m_FileSystem = fileSystem;
		m_FullPath = fullPath;
		m_Text.text = fileName;

		m_Text.color = fileName.StartsWith("Auto") ? m_AutosaveColor : m_ManualSaveColor;
	}


	public void Load()
	{
		m_FileSystem.LoadFromFullPath(m_FullPath);
	}

	public void OnPointerEnter(PointerEventData eventData)
	{
		m_IsMouseHovering = true;
		ShowInfoButton();
	}

	public void OnPointerExit(PointerEventData eventData)
	{
    // This check is nessesary because the file info button is ontop of this button, but only the top most button contains the mouse,
    // IE, OnPointerExit will trigger if the mouse leaves the button, or enters the ui button
    bool hovering = RectTransformUtility.RectangleContainsScreenPoint((RectTransform)transform, Input.mousePosition);
		
		// If the mouse is still over the file button
		if (hovering)
		{
			// Select the file button so the hilight stays on
			EventSystem.current.SetSelectedGameObject(gameObject);
			// Return without deselecting so the info button stays up
			return;
		}

		Deselect();
	}

	public void Update()
	{
    bool hovering = RectTransformUtility.RectangleContainsScreenPoint((RectTransform)transform, Input.mousePosition);
		
		// If the mouse moved outside the file button, but we are still in the hovered state; unselect button
		// This case is mainly for when the mouse was over the info button and just left
		if (m_IsMouseHovering && !hovering)
			Deselect();
	}

	private void Deselect()
	{
		// Unselect the file button if it was previously selected
		EventSystem.current.SetSelectedGameObject(null);
		m_IsMouseHovering = false;
		HideInfoButton();
	}

	private void ShowInfoButton()
	{
		// Scale text boxes to left a bit to make room for the info button
		m_Text.rectTransform.offsetMax = new Vector2(-m_VisibleRectTransformWidth, m_Text.rectTransform.offsetMax.y);
		m_TimeStamp.rectTransform.offsetMax = new Vector2(-m_VisibleRectTransformWidth, m_TimeStamp.rectTransform.offsetMax.y);
		// Show info button
		m_InfoButton.SetActive(true);
	}

	private void HideInfoButton()
	{
		// Scale text boxes back to normal
		m_Text.rectTransform.offsetMax = new Vector2(-m_HiddenRectTransformWidth, m_Text.rectTransform.offsetMax.y);
		m_TimeStamp.rectTransform.offsetMax = new Vector2(-m_HiddenRectTransformWidth, m_TimeStamp.rectTransform.offsetMax.y);
		// Hide info button
		m_InfoButton.SetActive(false);
	}
}