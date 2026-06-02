using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class DealButtonAnim : MonoBehaviour
{
    private Button dealButton;
    private CanvasGroup canvasGroup;

    void Start()
    {
        dealButton = GetComponent<Button>();
        
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) 
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
        
        dealButton.onClick.AddListener(OnDealClicked);
    }

    void OnDealClicked()
    {
        // 1. Button dabaate hi click hona band
        dealButton.interactable = false;
        
        // 2. Mouse clicks ko completely block kar do taaki invisible button par click na ho
        canvasGroup.blocksRaycasts = false; 

        // 3. Punch animation aur uske baad Fade
        transform.DOPunchScale(new Vector3(-0.1f, -0.1f, 0f), 0.15f, 1, 0.5f).OnComplete(() => 
        {
            // Sirf transparency 0 kar rahe hain, object ko band nahi kar rahe
            canvasGroup.DOFade(0, 0.3f); 
        });
    }
}