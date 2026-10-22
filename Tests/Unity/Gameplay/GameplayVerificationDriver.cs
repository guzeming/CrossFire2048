#if UNITY_EDITOR
using System;
using System.Collections;
using OperationBlacktide.Client.Features.Training;
using OperationBlacktide.Client.Gameplay.Characters;
using OperationBlacktide.Client.Gameplay.Combat;
using UnityEditor;
using UnityEngine;

public sealed class GameplayVerificationDriver : MonoBehaviour
{
    private IEnumerator Start()
    {
        yield return null;
        yield return null;
        try
        {
            var scene = FindObjectOfType<TrainingSceneController>();
            var gun = scene.Player.GetComponent<WeaponController>();
            Check(gun.Ammo.InfiniteReserve && gun.Throwables.InfiniteSupply && gun.AutoRechargeTaser,
                "Training supply policy was not applied.");
            gun.Ammo.TryConsume();
            scene.Vitals.ApplyDamage(20);
            scene.Player.Respawn();
            Check(scene.Vitals.Health == scene.Vitals.MaxHealth && gun.Ammo.Magazine == gun.Ammo.Capacity,
                "Training respawn did not restore supplies and health.");

            // An ordinary character is damageable without adding a training target.
            var fixture = new GameObject("Ordinary combat character");
            var health = fixture.AddComponent<CharacterVitals>();
            health.SetArmor(100);
            Damageable receiver = health;
            Check(receiver.ApplyDamage(20) == 10 && health.Health == 90 && health.Armor == 90,
                "Common damage receiver bypassed armor.");
            Check(receiver.ApplyDamage(10, true) == 10 && health.Armor == 90, "Fire armor bypass changed.");
            var motor = fixture.AddComponent<CharacterMotor>();
            motor.Initialize(null, Vector3.up * 100, Quaternion.identity);
            Check(health.Health == 80, "Common motor silently applied training respawn rules.");
            var ordinaryGun = fixture.AddComponent<WeaponController>();
            Check(!ordinaryGun.InfiniteAmmoReserve && !ordinaryGun.InfiniteThrowables && !ordinaryGun.AutoRechargeTaser,
                "Training supply defaults leaked into ordinary combat.");
            Destroy(fixture);
            Debug.Log("GAMEPLAY_MODE_POLICY_PASS: common damage receiver, finite defaults, training supplies and respawn.");
            new GameObject("Top Down Verification").AddComponent<TopDownVerificationDriver>();
            Destroy(gameObject);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
#endif
