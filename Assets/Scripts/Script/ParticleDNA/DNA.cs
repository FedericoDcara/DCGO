using System.Collections.Generic;
using UnityEngine;

// Restored from NucleusDNAEffect (removed for open source). Drives jogress DNA particle shape.
public class DNA : MonoBehaviour
{
    private ParticleSystem PS;

    float density = 20;
    float curve = -7;

    List<Vector3> dnaPoints = new List<Vector3>();

    [SerializeField] float rotateSpeed = 3;

    System.Random random;
    int count = 0;

    void OnEnable()
    {
        random = new System.Random();
        dnaPoints.Clear();

        PS = GetComponent<ParticleSystem>();
        if (PS == null)
            return;

        MakeDNA(Vector3.zero, 100f);
        MakeLadder(100f);

        transform.localRotation = Quaternion.identity;
        count = 0;
    }

    void MakeDNA(Vector3 position, float length)
    {
        float addp = 0;
        float height = 0;

        for (float p = 0; p < length * density; p++)
        {
            height += 1 / density;
            float pX = 5;
            float pY = height + (random.Next(-10, 10) / 10f);
            float pZ = 0;

            Vector3 point = new Vector3(pX, 0, 0);
            Vector3 center = Vector3.zero;

            addp += 180 + (curve / density);

            Vector3 r = rotateAround(point, center, addp);
            addp %= 360;
            pX = r.x;
            pZ = r.y;

            dnaPoints.Add(new Vector3(pX, pY, pZ));
        }
    }

    void MakeLadder(float length)
    {
        float addp = 0;
        float height = 0;
        float ladderspace = 4;
        for (float p = 0; p <= length / ladderspace; p++)
        {
            for (float i = 0; i < density * 2f; i++)
            {
                float pX = random.Next(-50, 50) / 10f;
                float pY = height + (random.Next(-4, 4) / 10f);

                Vector3 point = new Vector3(pX, 0, 0);
                Vector3 center = Vector3.zero;
                addp += 180f;

                Vector3 r = rotateAround(point, center, addp);
                addp %= 360;
                pX = r.x;
                float pZ = r.y;

                dnaPoints.Add(new Vector3(pX, pY, pZ));
            }
            addp += curve * ladderspace;
            addp %= 360;
            height += ladderspace;
        }
    }

    Vector3 rotateAround(Vector3 point, Vector3 center, float angle)
    {
        angle = angle * (Mathf.PI / 180f);
        float rotatedX = Mathf.Cos(angle) * (point.x - center.x) - Mathf.Sin(angle) * (point.y - center.y) + center.x;
        float rotatedY = Mathf.Sin(angle) * (point.x - center.x) + Mathf.Cos(angle) * (point.y - center.y) + center.y;
        return new Vector3(rotatedX, rotatedY);
    }

    void Update()
    {
        if (PS == null)
            return;

        ParticleSystem.Particle[] ps = new ParticleSystem.Particle[PS.main.maxParticles];
        int pCount = PS.GetParticles(ps);

        int limit = Mathf.Min(dnaPoints.Count, pCount);
        for (int i = 0; i < limit; i++)
        {
            ps[i].position = dnaPoints[i];
        }

        PS.SetParticles(ps, pCount);

        if (!PS.isPlaying)
        {
            PS.Play();
        }

        count++;

        if (count >= 40)
        {
            // RotateAroundLocal took radians; convert for modern Transform.Rotate
            transform.Rotate(Vector3.up, rotateSpeed * Mathf.Rad2Deg * Time.deltaTime, Space.Self);
        }
    }
}
