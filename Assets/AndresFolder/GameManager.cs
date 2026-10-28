using UnityEngine;
using System.Collections.Generic;
using TMPro;
public class GameManager : MonoBehaviour
{
    [Header("Enemigos")]
    [SerializeField] private GameObject enemyPrefab;
    [SerializeField] private GameObject chasingEnemyPrefab;
    [SerializeField, Min(1)] private int enemyCount = 5;
    [SerializeField, Min(0.1f)] private float spawnInterval = 2f;
    [SerializeField] private Transform enemyContainer;

    [Header("Puntos de aparición")]
    [SerializeField] private GameObject[] spawnPoints;

    [Header("Rango para desactivar")]
    [SerializeField] private Vector3 minimumActivePosition = new Vector3(-25f, -10f, -25f);
    [SerializeField] private Vector3 maximumActivePosition = new Vector3(25f, 10f, 25f);

    [Header("Tiempo de partida")]
    [SerializeField, Min(0.1f)] private float gameDuration = 60f;
    [SerializeField, Min(0f)] private float bookTimeBonus = 60f;
    [SerializeField] private MoveController player;
    [SerializeField] private GameObject book1;
    [SerializeField] private GameObject book2;
    [SerializeField] private GameObject book3;
    [SerializeField] private TMP_Text timeText;

    private readonly List<GameObject> enemyPool = new List<GameObject>();
    private readonly List<GameObject> chasingEnemyPool = new List<GameObject>();
    private float elapsedTime;
    private float spawnTimer;
    private float chasingSpawnTimer;
    private int lastDisplayedSecond = -1;
    private bool spawning = true;
    public SpellResetBehaviour spellResetBehaviour;
    [SerializeField, Min(0)] private int chasingEnemyCount = 1;
    [SerializeField, Min(0.1f)] private float chasingSpawnInterval = 15f;
    public bool playerIsDead = false;
    public bool gameWin = false;
    private bool gameEnded;

    private void Awake()
    {
        CreateEnemyPool();
        CreateChasingEnemyPool();
        playerIsDead = false;
    }

    private void Start()
    {
        SpawnAvailableEnemies();
    }

    private void Update()
    {
        if (!spawning)
        {
            return;
        }
        if (!playerIsDead)
        {
            elapsedTime += Time.deltaTime;
        }
        PrintRemainingTime(playerIsDead);

        if (elapsedTime >= gameDuration)
        {
            StopSpawning(false);
            return;
        }

        DeactivateEnemiesOutsideRange();

        spawnTimer += Time.deltaTime;
        if (spawnTimer >= spawnInterval)
        {
            spawnTimer = 0f;
            SpawnAvailableEnemies();
        }

        chasingSpawnTimer += Time.deltaTime;
        if (chasingSpawnTimer >= chasingSpawnInterval)
        {
            chasingSpawnTimer = 0f;
            SpawnAvailableChasingEnemies();
        }
    }

    private void PrintRemainingTime(bool canSpell)
    {
        if (!canSpell)
        {
            int remainingSeconds = Mathf.Max(0, Mathf.CeilToInt(gameDuration - elapsedTime));

            if (remainingSeconds == lastDisplayedSecond)
            {
                return;
            }

            lastDisplayedSecond = remainingSeconds;

            if (timeText != null)
            {
                timeText.text = $"Tiempo para el bucle: {remainingSeconds}";
            }
        }
    }

    public void AddBookTime()
    {
        gameDuration += bookTimeBonus;
        Debug.Log(
            $"Libro recogido. Se añadieron {bookTimeBonus:0} segundos. "
            + $"Tiempo total: {gameDuration:0} segundos.",
            this);
    }

    private void CreateEnemyPool()
    {
        if (enemyPrefab == null)
        {
            Debug.LogError("GameManager: asigna un prefab en Enemy Prefab.", this);
            return;
        }

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogError("GameManager: agrega al menos un punto en Spawn Points.", this);
            return;
        }

        for (int i = 0; i < spawnPoints.Length; i++)
        {
            if (spawnPoints[i] == null)
            {
                Debug.LogError($"GameManager: el punto de aparición {i} está vacío.", this);
                return;
            }
        }

        for (int i = 0; i < enemyCount; i++)
        {
            GameObject enemy = Instantiate(enemyPrefab);

            if (IsSceneObject(enemyContainer))
            {
                enemy.transform.SetParent(enemyContainer);
            }

            enemy.SetActive(false);
            enemyPool.Add(enemy);
        }
    }

    private static bool IsSceneObject(Transform target)
    {
        return target != null && target.gameObject.scene.IsValid();
    }

    private void SpawnAvailableEnemies()
    {
        foreach (GameObject enemy in enemyPool)
        {
            if (enemy.activeSelf)
            {
                continue;
            }

            Transform spawnPoint = GetRandomSpawnPoint();
            enemy.transform.SetPositionAndRotation(
                spawnPoint.position,
                spawnPoint.rotation);

            MoveBallEnemy fireball = enemy.GetComponent<MoveBallEnemy>();
            if (fireball != null)
            {
                fireball.SetDirection(spawnPoint.forward);
            }

            enemy.SetActive(true);
        }
    }

    private void CreateChasingEnemyPool()
    {
        if (chasingEnemyPrefab == null || chasingEnemyCount == 0)
        {
            return;
        }

        for (int i = 0; i < chasingEnemyCount; i++)
        {
            GameObject enemy = Instantiate(chasingEnemyPrefab);

            if (IsSceneObject(enemyContainer))
            {
                enemy.transform.SetParent(enemyContainer);
            }

            ChasingBallEnemy chasingEnemy = enemy.GetComponent<ChasingBallEnemy>();
            if (chasingEnemy == null)
            {
                Debug.LogError("GameManager: el prefab perseguidor no tiene ChasingBallEnemy.", this);
                Destroy(enemy);
                continue;
            }

            if (player != null)
            {
                chasingEnemy.SetPlayer(player.transform);
            }

            enemy.SetActive(false);
            chasingEnemyPool.Add(enemy);
        }
    }

    private void SpawnAvailableChasingEnemies()
    {
        foreach (GameObject enemy in chasingEnemyPool)
        {
            if (enemy.activeSelf)
            {
                continue;
            }

            enemy.transform.SetPositionAndRotation(
                GetRandomSpawnPoint().position,
                Quaternion.identity);
            enemy.SetActive(true);
            break;
        }
    }

    private void DeactivateEnemiesOutsideRange()
    {
        Vector3 minimum = Vector3.Min(minimumActivePosition, maximumActivePosition);
        Vector3 maximum = Vector3.Max(minimumActivePosition, maximumActivePosition);

        foreach (GameObject enemy in enemyPool)
        {
            if (enemy.activeSelf && !IsInsideRange(enemy.transform.position, minimum, maximum))
            {
                enemy.SetActive(false);
            }
        }

        foreach (GameObject enemy in chasingEnemyPool)
        {
            if (enemy.activeSelf && !IsInsideRange(enemy.transform.position, minimum, maximum))
            {
                enemy.SetActive(false);
            }
        }
    }

    private Transform GetRandomSpawnPoint()
    {
        return spawnPoints[Random.Range(0, spawnPoints.Length)].transform;
    }

    private static bool IsInsideRange(Vector3 position, Vector3 minimum, Vector3 maximum)
    {
        return position.x >= minimum.x && position.x <= maximum.x
            && position.y >= minimum.y && position.y <= maximum.y
            && position.z >= minimum.z && position.z <= maximum.z;
    }

    private void StopSpawning(bool won)
    {
        if (gameEnded) return;
        gameEnded = true;
        spawning = false;

        foreach (GameObject enemy in enemyPool)
        {
            enemy.SetActive(false);
        }

        foreach (GameObject enemy in chasingEnemyPool)
        {
            enemy.SetActive(false);
        }

        if (spellResetBehaviour != null)
        {
            spellResetBehaviour.PlaySequence(won);
        }
        else
        {
            Debug.LogError("GameManager: falta asignar SpellResetBehaviour.", this);
        }
    }

    public void StartNewGame()
    {
        if (book1 != null)
        {
            book1.SetActive(true);
        }

        if (player != null)
        {
            player.ResetBook();
            player.ResetDash();
            player.ResetReflect();
        }

        if (book2 != null)
        {
            book2.SetActive(true);
        }

        if (book3 != null)
        {
            book3.SetActive(true);
        }

        Debug.Log("Nueva partida: libros y habilidades reiniciados.", this);
    }
    public void EndGameBecauseEnemyDied()
    {
        StopSpawning(true);
    }
}
