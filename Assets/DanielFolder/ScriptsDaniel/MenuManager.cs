
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

public class MenuManager : MonoBehaviour
{
    [Header("Scene Settings")]
    public string playScene;
    public string mainMenuScene;

    [Header("UI Settings")]
    public GameObject pauseMenu;
    public GameObject pauseButton;
    public GameObject eventSystem;
    public GameObject resumeButton;
    public GameObject credits;
    public GameObject menuPCE;
    public GameObject tutorial;


    public InputAction pauseAction;

    private bool isPaused = false;

    private void OnEnable()
    {
        // Crear un EventSystem solo si no existe uno activo.
        if (EventSystem.current == null)
        {
            Instantiate(eventSystem);
        }

        pauseAction.performed += OnPausePerformed;
        pauseAction.Enable();
    }

    private void OnDisable()
    {
        pauseAction.performed -= OnPausePerformed;
        pauseAction.Disable();
    }

    private void OnPausePerformed(InputAction.CallbackContext ctx)
    {
        // Evitar pausar desde el menú principal.
        if (SceneManager.GetActiveScene().name == mainMenuScene)
            return;

        PauseGame(false);
    }

    public void PlayGame()
    {
        tutorial.SetActive(false);
        Time.timeScale = 1f;
        isPaused = false;

        SceneManager.LoadScene(playScene);
    }

    public void ExitGame()
    {
        Application.Quit();
    }

    public void ShowCredits()
    {
        credits.SetActive(true);
        menuPCE.SetActive(false);

    }
    public void Tutorial()
    {
        tutorial.SetActive(true);
        menuPCE?.SetActive(false);

    }
    public void ReturnCredits()
    {
        credits.SetActive(false);
        menuPCE.SetActive(true);
    }

    public void PauseGame(bool dead)
    {
        isPaused = !isPaused;
        if (dead)
        {
            resumeButton.SetActive(false);
        }
        else
        {
            resumeButton.SetActive(true);
        }

        pauseMenu.SetActive(isPaused);
        pauseButton.SetActive(!isPaused);

        Time.timeScale = isPaused ? 0f : 1f;
        Cursor.visible = isPaused;
        Cursor.lockState = isPaused
            ? CursorLockMode.None
            : CursorLockMode.Locked;

        Debug.Log(Time.timeScale);
    }

    public void MainMenu()
    {
        Time.timeScale = 1f;
        isPaused = false;

        SceneManager.LoadScene(mainMenuScene);
    }
}
