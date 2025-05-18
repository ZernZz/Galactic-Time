using Unity.Netcode;
using UnityEngine;

public class Meteor : NetworkBehaviour
{
    public float moveSpeed = 10f;
    public int health = 5;

    public MeteorSpawner spawner;

    private Vector3 targetPosition;
    private bool hasTarget = false;
    private bool isDestroyed = false;

    public void SetTarget(Vector3 target)
    {
        targetPosition = target;
        hasTarget = true;
    }

    private void Update()
    {
        if (!hasTarget) return;

        transform.position += Vector3.forward * moveSpeed * Time.deltaTime;
        transform.Rotate(Vector3.up * 100f * Time.deltaTime);

        if (Vector3.Distance(transform.position, targetPosition) < 0.5f)
        {
            DestroySelf(0); // 0 = ไม่มีผู้ยิง
        }
    }

    public void TakeHit(ulong shooterClientId)
    {
        if (isDestroyed) return;

        health--;
        if (health <= 0)
        {
            DestroySelf(shooterClientId);
        }
    }

    private void DestroySelf(ulong shooterClientId)
    {
        if (isDestroyed) return;
        isDestroyed = true;

        if (spawner != null)
        {
            spawner.NotifyMeteorDestroyed(gameObject);
        }

        // ✅ เพิ่มคะแนนให้คนที่ยิงโดน
        if (shooterClientId != 0 && ScoreManager.Instance != null)
        {
            ScoreManager.Instance.AddScoreToPlayer(shooterClientId, 50);
        }

        Destroy(gameObject);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Bullet"))
        {
            Bullet bullet = other.GetComponent<Bullet>();
            if (bullet != null)
            {
                TakeHit(bullet.ownerClientId); // ✅ แก้ตรงนี้
            }

            Destroy(other.gameObject);
        }
    }
}
