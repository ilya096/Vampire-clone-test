using UnityEngine;

[CreateAssetMenu(fileName = "EnemySpawnConfig", menuName = "Configs/EnemySpawnConfig")]
public class EnemySpawnConfig : ScriptableObject
{
    [SerializeField] private float _interval = 1.25f;
    [SerializeField] private float _spawnRadius = 9f;
    [SerializeField] private float _enemySpeed = 2.5f;
    [SerializeField] private float _enemyHealth = 20f;
    [SerializeField] private int _maxEnemies;
    [SerializeField] private int _enemyAttack = 1;
    [SerializeField] private float _enemyRange = 1.5f;
    [SerializeField] private float _enemyAttackInterval = 1f;

    public float Interval => _interval;
    public float SpawnRadius => _spawnRadius;
    public float EnemySpeed => _enemySpeed;
    public float EnemyHealth => _enemyHealth;
    public int MaxEnemies => _maxEnemies;
    public int EnemyAttack => _enemyAttack;
    public float EnemyRange => _enemyRange;
    public float EnemyAttackInterval => _enemyAttackInterval;
}
