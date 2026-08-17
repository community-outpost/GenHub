#!/usr/bin/env python3
"""
Synthetic C&C Generals / Zero Hour Test Dataset Generator
Generates deterministic, authentic mod assets for single-thread benchmarking across Python, Go, and C# ModBuilder ports.
"""

import os
import sys
import struct
import random
import json
import argparse
from pathlib import Path

# Deterministic random seed
RANDOM_SEED = 1337

def generate_ini_content(num_objects: int = 5) -> str:
    """Generates authentic SAGE INI game rules with nested behaviors, weapons, and armor."""
    lines = [
        "; ------------------------------------------------------------------------------",
        "; C&C Generals / Zero Hour Synthetic Benchmark Game Rules",
        "; ------------------------------------------------------------------------------",
        ""
    ]
    
    factions = ["America", "China", "GLA"]
    armor_types = ["TankArmor", "StructureArmor", "InfantryArmor", "AircraftArmor"]
    
    for i in range(num_objects):
        faction = random.choice(factions)
        obj_name = f"{faction}VehicleUnit_{i:04d}"
        armor = random.choice(armor_types)
        hp = random.randint(200, 1500)
        cost = random.randint(400, 2500)
        build_time = random.randint(5, 30)
        
        lines.extend([
            f"Object {obj_name}",
            f"  ; Unit design parameters for {obj_name}",
            f"  SelectPortrait         = SN{obj_name}_L",
            f"  ButtonImage            = SN{obj_name}",
            f"  Side                   = {faction}",
            f"  EditorSorting          = VEHICLE",
            f"  BuildCost              = {cost}",
            f"  BuildTime              = {build_time}",
            f"  VisionRange            = 150.0",
            f"  ShroudClearingRange    = 200.0",
            f"  TransportSlotCount     = 2",
            f"  ArmorSet",
            f"    Conditions           = None",
            f"    Armor                = {armor}",
            f"    DamageFX             = TankDamageFX",
            f"  End",
            f"  Body = ActiveBody ModuleTag_01",
            f"    MaxHealth            = {hp}.0",
            f"    InitialHealth        = {hp}.0",
            f"    SubdualDamageCap     = 1000",
            f"  End",
            f"  Behavior = AIUpdateInterface ModuleTag_02",
            f"    AutoAcquireEnemiesWhenIdle = Yes",
            f"  End",
            f"  Locomotor = SET_NORMAL {obj_name}Locomotor",
            f"  Behavior = PhysicsBehavior ModuleTag_03",
            f"    Mass                 = 40.0",
            f"  End",
            f"  Draw = W3DModelDraw ModuleTag_04",
            f"    DefaultConditionState",
            f"      Model              = {obj_name}_SKN",
            f"      Turret             = TURRET01",
            f"      WeaponFireFXBone   = PRIMARY MUZZLE",
            f"    End",
            f"    ConditionState       = REALLYDAMAGED",
            f"      Model              = {obj_name}_SKN_D",
            f"      ParticleSysBone    = SMOKE01 SmokeFactionSmall",
            f"    End",
            f"    ConditionState       = RUBBLE",
            f"      Model              = {obj_name}_SKN_R",
            f"    End",
            f"  End",
            f"  Geometry               = BOX",
            f"  GeometryMajorRadius    = 15.0",
            f"  GeometryMinorRadius    = 10.0",
            f"  GeometryHeight         = 12.0",
            f"  GeometryIsSmall        = Yes",
            f"  Shadow                 = SHADOW_VOLUME",
            f"End",
            ""
        ])
    return "\n".join(lines)


def generate_tga_file(file_path: str, width: int, height: int, has_alpha: bool = True):
    """Generates an uncompressed 24-bit RGB or 32-bit RGBA TGA file."""
    os.makedirs(os.path.dirname(file_path), exist_ok=True)
    bpp = 32 if has_alpha else 24
    
    descriptor = 8 if has_alpha else 0
    header = struct.pack(
        "<BBBHHBHHHHBB",
        0, 0, 2,  # Image type 2: Uncompressed RGB(A)
        0, 0, 0,
        0, 0, width, height, bpp, descriptor
    )
    
    pixel_bytes = os.urandom(width * height * (4 if has_alpha else 3))
    with open(file_path, "wb") as f:
        f.write(header)
        f.write(pixel_bytes)


def generate_csf_and_str(csf_path: str, str_path: str, num_labels: int = 100, language_id: int = 0):
    """Generates a binary CSF file and matching plaintext .str file."""
    os.makedirs(os.path.dirname(csf_path), exist_ok=True)
    os.makedirs(os.path.dirname(str_path), exist_ok=True)
    
    labels = []
    str_lines = []
    
    for i in range(num_labels):
        lbl_name = f"GUI:BenchmarkLabel_{i:05d}"
        lbl_val = f"Unit Designation Alpha-{i:05d}: Armed and operational for combat."
        labels.append((lbl_name, lbl_val))
        str_lines.append(f"{lbl_name}\n\"{lbl_val}\"\nEnd\n")
    
    # Write .str
    with open(str_path, "w", encoding="utf-8") as f:
        f.write("\n".join(str_lines))
        
    # Write binary .csf
    csf_bytes = bytearray()
    csf_bytes.extend(struct.pack("<4sIIIII", b" FSC", 3, len(labels), len(labels), 0, language_id))
    
    for lbl_name, lbl_val in labels:
        lbl_name_bytes = lbl_name.encode("ascii")
        csf_bytes.extend(struct.pack("<4sII", b" LBL", 1, len(lbl_name_bytes)))
        csf_bytes.extend(lbl_name_bytes)
        
        val_chars = [ord(c) for c in lbl_val]
        inverted_bytes = bytearray()
        for c in val_chars:
            inv_c = (~c) & 0xFFFF
            inverted_bytes.extend(struct.pack("<H", inv_c))
            
        csf_bytes.extend(struct.pack("<4sI", b" STR", len(val_chars)))
        csf_bytes.extend(inverted_bytes)
        
    with open(csf_path, "wb") as f:
        f.write(csf_bytes)


def generate_wav_file(file_path: str, duration_sec: float = 0.5, sample_rate: int = 22050):
    """Generates a valid PCM WAV audio file."""
    os.makedirs(os.path.dirname(file_path), exist_ok=True)
    num_samples = int(duration_sec * sample_rate)
    num_channels = 1
    bits_per_sample = 16
    bytes_per_sample = bits_per_sample // 8
    block_align = num_channels * bytes_per_sample
    byte_rate = sample_rate * block_align
    data_size = num_samples * block_align
    
    raw_data = bytearray()
    for i in range(num_samples):
        sample_val = int(16000 * (1 if (i % (sample_rate // 440)) < (sample_rate // 880) else -1))
        raw_data.extend(struct.pack("<h", sample_val))
        
    header = struct.pack(
        "<4sI4s4sIHHIIHH4sI",
        b"RIFF", 36 + data_size, b"WAVE",
        b"fmt ", 16, 1, num_channels, sample_rate, byte_rate, block_align, bits_per_sample,
        b"data", data_size
    )
    
    with open(file_path, "wb") as f:
        f.write(header)
        f.write(raw_data)


def generate_w3d_file(file_path: str, num_vertices: int = 120):
    """Generates a valid Westwood W3D mesh binary model file."""
    os.makedirs(os.path.dirname(file_path), exist_ok=True)
    vertex_data = bytearray()
    for _ in range(num_vertices):
        x = random.uniform(-10.0, 10.0)
        y = random.uniform(-10.0, 10.0)
        z = random.uniform(0.0, 15.0)
        vertex_data.extend(struct.pack("<fff", x, y, z))
        
    num_triangles = num_vertices // 3
    tri_data = bytearray()
    for t in range(num_triangles):
        v0 = t * 3
        v1 = t * 3 + 1
        v2 = t * 3 + 2
        tri_data.extend(struct.pack("<III", v0, v1, v2))
        
    vert_chunk = struct.pack("<II", 0x0002, len(vertex_data)) + vertex_data
    tri_chunk = struct.pack("<II", 0x0005, len(tri_data)) + tri_data
    
    mesh_payload = vert_chunk + tri_chunk
    mesh_chunk = struct.pack("<II", 0x0000, len(mesh_payload)) + mesh_payload
    
    with open(file_path, "wb") as f:
        f.write(mesh_chunk)


def generate_tier_dataset(target_dir: str, tier: int = 1):
    """
    Generates a full, authentic Generals ModBuilder project at target_dir.
    Tier 1 (Small): 10 files, ~5 MB
    Tier 2 (Medium): 100 files, ~50 MB
    Tier 3 (Large): 1,000 files, ~500 MB
    """
    random.seed(RANDOM_SEED)
    os.makedirs(target_dir, exist_ok=True)
    
    tier_configs = {
        1: {"ini": 4, "tga": 2, "csf": 1, "wav": 2, "w3d": 1, "tga_res": [512]},
        2: {"ini": 40, "tga": 30, "csf": 5, "wav": 15, "w3d": 10, "tga_res": [256, 512, 1024]},
        3: {"ini": 350, "tga": 350, "csf": 30, "wav": 170, "w3d": 100, "tga_res": [512, 1024, 2048]}
    }
    
    cfg = tier_configs.get(tier, tier_configs[1])
    game_files_dir = os.path.join(target_dir, "GameFilesEdited")
    config_dir = os.path.join(target_dir, "config")
    
    art_textures = os.path.join(game_files_dir, "Art", "Textures")
    art_models = os.path.join(game_files_dir, "Art", "Models")
    data_ini = os.path.join(game_files_dir, "Data", "INI", "Object")
    data_audio = os.path.join(game_files_dir, "Data", "Audio", "Sounds")
    data_english = os.path.join(game_files_dir, "Data", "English")
    
    os.makedirs(art_textures, exist_ok=True)
    os.makedirs(art_models, exist_ok=True)
    os.makedirs(data_ini, exist_ok=True)
    os.makedirs(data_audio, exist_ok=True)
    os.makedirs(data_english, exist_ok=True)
    os.makedirs(config_dir, exist_ok=True)
    
    created_files = []
    
    # 1. Generate INI files
    for i in range(cfg["ini"]):
        path = os.path.join(data_ini, f"UnitRules_{i:04d}.ini")
        content = generate_ini_content(num_objects=random.randint(3, 10))
        with open(path, "w", encoding="utf-8") as f:
            f.write(content)
        created_files.append(path)
        
    # 2. Generate TGA Textures
    for i in range(cfg["tga"]):
        res = random.choice(cfg["tga_res"])
        has_alpha = (i % 2 == 0)
        path = os.path.join(art_textures, f"Texture_{i:04d}_{res}x{res}.tga")
        generate_tga_file(path, res, res, has_alpha=has_alpha)
        created_files.append(path)
        
    # 3. Generate CSF & STR String Tables
    for i in range(cfg["csf"]):
        csf_path = os.path.join(data_english, f"GeneralsStrings_{i:03d}.csf")
        str_path = os.path.join(data_english, f"GeneralsStrings_{i:03d}.str")
        generate_csf_and_str(csf_path, str_path, num_labels=100 + i * 50)
        created_files.append(csf_path)
        created_files.append(str_path)
        
    # 4. Generate WAV Audio
    for i in range(cfg["wav"]):
        path = os.path.join(data_audio, f"VoiceLine_{i:04d}.wav")
        generate_wav_file(path, duration_sec=random.uniform(0.3, 1.5))
        created_files.append(path)
        
    # 5. Generate W3D 3D Models
    for i in range(cfg["w3d"]):
        path = os.path.join(art_models, f"UnitModel_{i:04d}.w3d")
        generate_w3d_file(path, num_vertices=random.randint(60, 300))
        created_files.append(path)
        
    # 6. Generate JSON Mod Configurations
    bundle_items_cfg = {
        "bundles": {
            "version": 1,
            "itemsPrefix": "",
            "itemsSuffix": "",
            "items": [
                {
                    "name": "CoreGameData",
                    "big": True,
                    "bigSuffix": "",
                    "files": [
                        {
                            "sourceParent": "GameFilesEdited",
                            "source": "Data/**/*.*",
                            "target": "Data/**/*.*"
                        }
                    ]
                },
                {
                    "name": "CoreArtTextures",
                    "big": True,
                    "bigSuffix": "",
                    "files": [
                        {
                            "sourceParent": "GameFilesEdited",
                            "source": "Art/Textures/**/*.tga",
                            "target": "Art/Textures/**/*.dds",
                            "params": {
                                "format": "dds",
                                "mipmaps": True
                            }
                        },
                        {
                            "sourceParent": "GameFilesEdited",
                            "source": "Art/Models/**/*.w3d",
                            "target": "Art/Models/**/*.w3d"
                        }
                    ]
                }
            ]
        }
    }
    
    with open(os.path.join(config_dir, "ModBundleItems.json"), "w", encoding="utf-8") as f:
        json.dump(bundle_items_cfg, f, indent=2)
        
    bundle_packs_cfg = {
        "bundles": {
            "version": 1,
            "packsPrefix": "",
            "packsSuffix": "",
            "packs": [
                {
                    "name": "MainReleasePack",
                    "itemNames": ["CoreGameData", "CoreArtTextures"]
                }
            ]
        }
    }
    
    with open(os.path.join(config_dir, "ModBundlePacks.json"), "w", encoding="utf-8") as f:
        json.dump(bundle_packs_cfg, f, indent=2)
        
    folders_cfg = {
        "folders": {
            "version": 1,
            "buildDir": ".Build",
            "releaseDir": ".Release"
        }
    }
    
    with open(os.path.join(config_dir, "ModFolders.json"), "w", encoding="utf-8") as f:
        json.dump(folders_cfg, f, indent=2)
        
    mod_json_files = {
        "build": {
            "version": 1,
            "files": [
                "config/ModBundleItems.json",
                "config/ModBundlePacks.json",
                "config/ModFolders.json"
            ]
        }
    }
    
    with open(os.path.join(target_dir, "ModJsonFiles.json"), "w", encoding="utf-8") as f:
        json.dump(mod_json_files, f, indent=2)
        
    total_bytes = sum(os.path.getsize(p) for p in created_files if os.path.exists(p))
    print(f"Generated Tier {tier} Dataset at {target_dir}: {len(created_files)} files, {total_bytes / (1024*1024):.2f} MB")
    return created_files


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Synthetic ModBuilder Dataset Generator")
    parser.add_argument("--dir", default="/tmp/modbuilder_test_dataset", help="Target output directory")
    parser.add_argument("--tier", type=int, default=1, choices=[1, 2, 3], help="Dataset tier (1=Small, 2=Medium, 3=Large)")
    args = parser.parse_args()
    
    generate_tier_dataset(args.dir, args.tier)
