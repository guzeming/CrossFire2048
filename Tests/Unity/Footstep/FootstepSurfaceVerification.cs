#if UNITY_EDITOR
using System;
using System.Linq;
using OperationBlacktide.Client.Gameplay.Characters;
using OperationBlacktide.Client.Gameplay.Combat;
using UnityEngine;
using Object = UnityEngine.Object;

public static class FootstepSurfaceVerification
{
    private const float Dt = 1f / 60;
    private static readonly Vector3 Spawn = new Vector3(0, 100.08f, 0);

    public static void Run(CharacterMotor player, FootstepAudio audio, GameObject floor)
    {
        var surface = floor.AddComponent<ImpactSurface>();
        foreach (var sample in new[] {
            (ImpactKind.Concrete, "concrete_ct_", "land"), (ImpactKind.WoodPanel, "wood_", "wood_"),
            (ImpactKind.Wood, "wood_", "wood_"), (ImpactKind.Metal, "metal_solid_", "land_metal_solid_"),
            (ImpactKind.MetalGrate, "metal_grate_", "land_metal_grate_"), (ImpactKind.MetalBarrel, "metal_vent_", "land_metal_barrel_"),
            (ImpactKind.Glass, "glass_", "land_glass_"), (ImpactKind.Tile, "tile_", "land_tile_"),
            (ImpactKind.Dirt, "dirt_", "land_dirt_"), (ImpactKind.Sand, "sand_", "land_sand_"),
            (ImpactKind.Grass, "grass_", "land_grass_"), (ImpactKind.Plastic, "plastic_barrel_", "land_auto_"),
            (ImpactKind.Cardboard, "cardboard_box_", "land_cardboard_"), (ImpactKind.Rubber, "rubber_", "land_rubber_"),
            (ImpactKind.Cloth, "carpet_", "land_carpet_"), (ImpactKind.Water, "puddle_", "water_splash_") })
        {
            surface.Kind = sample.Item1;
            Settle(player, audio, Spawn);
            int before = audio.StepsPlayed;
            for (int i = 0; i < 12; i++) Move(player, audio, Vector2.up);
            Check(audio.StepsPlayed > before && audio.LastSurface == sample.Item1 && audio.LastClip.name.StartsWith(sample.Item2),
                "Wrong step sound on " + sample.Item1 + ": " + audio.LastClip.name + "/" + audio.LastSurface);
            int lands = audio.LandingsPlayed;
            Move(player, audio, Vector2.zero, true);
            for (int i = 0; i < 120; i++) Move(player, audio, Vector2.zero);
            Check(audio.LandingsPlayed == lands + 1 && audio.LastSurface == sample.Item1 && audio.LastClip.name.StartsWith(sample.Item3),
                "Wrong or repeated landing on " + sample.Item1);
        }
        surface.Kind = ImpactKind.Concrete;
        CheckWater(player, audio);
        CheckTransition(player, audio);
        CheckMapCrate(player, audio);
        Settle(player, audio, Spawn);
        Debug.Log("FOOTSTEP_SURFACES_PASS: 15 banks + wood panel, landings, concrete/wood/water transitions, water volumes, dry box above water, actual Dust II crate.");
    }

    private static void CheckWater(CharacterMotor player, FootstepAudio audio)
    {
        var water = new GameObject("Shallow water volume");
        water.transform.position = new Vector3(0, 100.05f, 0);
        var collider = water.AddComponent<BoxCollider>();
        collider.size = new Vector3(8, .7f, 8); collider.isTrigger = true;
        water.AddComponent<ImpactSurface>().Kind = ImpactKind.Water;
        Settle(player, audio, Spawn);
        for (int i = 0; i < 12; i++) Move(player, audio, Vector2.up);
        Check(audio.LastSurface == ImpactKind.Water && audio.LastClip.name.StartsWith("puddle_"), "Feet inside water did not splash");
        int lands = audio.LandingsPlayed;
        Move(player, audio, Vector2.zero, true);
        for (int i = 0; i < 120; i++) Move(player, audio, Vector2.zero);
        Check(audio.LandingsPlayed == lands + 1 && audio.LastClip.name.StartsWith("water_splash_"), "Water landing has no splash");
        int steps = audio.StepsPlayed;
        for (int i = 0; i < 90; i++) Move(player, audio, Vector2.zero);
        Check(audio.StepsPlayed == steps, "Idle in water plays steps");

        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.transform.position = new Vector3(0, 100.5f, 0); box.transform.localScale = new Vector3(4, 1, 4);
        box.AddComponent<ImpactSurface>().Kind = ImpactKind.WoodPanel;
        Settle(player, audio, new Vector3(0, 101.08f, 0));
        for (int i = 0; i < 12; i++) Move(player, audio, Vector2.up);
        Check(audio.LastSurface == ImpactKind.WoodPanel && audio.LastClip.name.StartsWith("wood_"), "Dry box above water splashes");
        box.SetActive(false); Object.Destroy(box);
        // An unrelated trigger cannot override the supporting concrete surface.
        water.GetComponent<ImpactSurface>().Kind = ImpactKind.Wood;
        Settle(player, audio, Spawn);
        for (int i = 0; i < 12; i++) Move(player, audio, Vector2.up);
        Check(audio.LastSurface == ImpactKind.Concrete, "Non-water trigger overrode solid ground");
        water.SetActive(false); Object.Destroy(water);
    }

    private static void CheckTransition(CharacterMotor player, FootstepAudio audio)
    {
        var wood = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wood.transform.position = new Vector3(0, 102.5f, 2); wood.transform.localScale = new Vector3(6, 1, 4);
        wood.AddComponent<ImpactSurface>().Kind = ImpactKind.WoodPanel;
        var sand = GameObject.CreatePrimitive(PrimitiveType.Cube);
        sand.transform.position = new Vector3(0, 102.5f, 6); sand.transform.localScale = new Vector3(6, 1, 4);
        sand.AddComponent<ImpactSurface>().Kind = ImpactKind.Sand;
        Settle(player, audio, new Vector3(0, 103.08f, .2f));
        bool woodPlayed = false, sandPlayed = false;
        for (int i = 0; i < 96; i++)
        {
            int before = audio.StepsPlayed;
            Move(player, audio, Vector2.up);
            if (audio.StepsPlayed == before) continue;
            if (player.transform.position.z < 4) woodPlayed |= audio.LastSurface == ImpactKind.WoodPanel;
            else sandPlayed |= audio.LastSurface == ImpactKind.Sand;
        }
        Check(woodPlayed && sandPlayed, "Walking across a material boundary did not switch immediately on the next contact");
        wood.SetActive(false); sand.SetActive(false); Object.Destroy(wood); Object.Destroy(sand);
    }

    private static void CheckMapCrate(CharacterMotor player, FootstepAudio audio)
    {
        foreach (var candidate in Object.FindObjectsOfType<MeshCollider>())
        {
            var renderer = candidate.GetComponent<Renderer>();
            if (renderer == null || !renderer.sharedMaterials.Any(m => m != null &&
                (m.name.Contains("dust_shipping_crate") || m.name.Contains("wooden_fruit_crate")))) continue;
            var bounds = candidate.bounds;
            for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
            {
                Vector3 origin = new Vector3(bounds.center.x + x * bounds.extents.x * .4f, bounds.max.y + 2,
                    bounds.center.z + z * bounds.extents.z * .4f);
                if (!Physics.Raycast(origin, Vector3.down, out var hit, bounds.size.y + 3, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore) || hit.collider != candidate || hit.normal.y < .9f) continue;
                var kind = ImpactSurfaceResolver.Resolve(hit);
                if (kind != ImpactKind.Wood && kind != ImpactKind.WoodPanel) continue;
                if (Physics.CheckCapsule(hit.point + Vector3.up * .36f, hit.point + Vector3.up * 1.5f,
                    .3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                Settle(player, audio, hit.point + Vector3.up * .08f);
                int before = audio.StepsPlayed;
                Vector2 direction = bounds.size.x > bounds.size.z ? Vector2.right : Vector2.up;
                for (int i = 0; i < 7; i++) Move(player, audio, direction);
                if (audio.StepsPlayed == before || audio.LastGroundCollider != candidate) continue;
                Check((audio.LastSurface == ImpactKind.WoodPanel || audio.LastSurface == ImpactKind.Wood) && audio.LastClip.name.StartsWith("wood_"),
                    "Real Dust II crate still uses concrete steps");
                Debug.Log("FOOTSTEP_DUSTII_CRATE_PASS: " + candidate.name + " at " + hit.point + " -> " + audio.LastClip.name);
                return;
            }
        }
        throw new Exception("No real walkable wooden Dust II crate was verified");
    }

    private static void Settle(CharacterMotor player, FootstepAudio audio, Vector3 position)
    {
        var controller = player.GetComponent<CharacterController>();
        player.Respawn(); controller.enabled = false;
        player.transform.SetPositionAndRotation(position, Quaternion.identity);
        controller.enabled = true;
        Physics.SyncTransforms();
        for (int i = 0; i < 30; i++) player.Move(Vector2.zero, false, false, Dt);
        Check(player.IsGrounded, "Surface fixture did not ground player at " + position);
        audio.ResetMotion();
    }
    private static void Move(CharacterMotor player, FootstepAudio audio, Vector2 direction, bool jump = false)
    { player.Move(direction, false, jump, Dt); audio.UpdateAudio(Dt, true); }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
#endif
