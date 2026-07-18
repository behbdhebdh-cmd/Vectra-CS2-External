#pragma once

#include <cstdint>

constexpr std::int32_t VECTRA_MENU_API_VERSION = 2;
constexpr std::int32_t VECTRA_MENU_MAX_MAPS = 24;
constexpr std::int32_t VECTRA_MENU_MAP_NAME = 32;

enum class VectraBoolSetting : std::int32_t {
    MasterEnabled, PrivateMatchAuthorized, EspEnabled, TeamCheckEnabled,
    TriggerEnabled, AimAssistEnabled, AimVisibilityCheckEnabled, DrawAimFov,
    CornerBoxes, DrawNames, DrawHealth, DrawDistance, DrawWeapons,
    DrawBombEsp, DrawItemEsp, DrawGrenadePrediction, DrawSnaplines,
    DrawOffscreenArrows, DrawRadar, DrawHeadMarker, HideOverlayFromCapture
};

enum class VectraIntSetting : std::int32_t {
    AimAssistFovPixels, AimAssistStrengthPercent, AimTargetPoint, AimPriority,
    AimMovement, AimActivation, AimActivationKey, EspTheme
};

enum class VectraDoubleSetting : std::int32_t { UiOpacity };
enum class VectraStringSetting : std::int32_t { GrenadePredictionMap };
enum class VectraMenuCommand : std::int32_t { SaveConfiguration = 1, LoadConfiguration = 2, Shutdown = 3 };

struct VectraMenuState {
    std::int32_t api_version;
    std::int32_t master_enabled;
    std::int32_t private_match_authorized;
    std::int32_t esp_enabled;
    std::int32_t team_check_enabled;
    std::int32_t trigger_enabled;
    std::int32_t aim_assist_enabled;
    std::int32_t aim_visibility_check_enabled;
    std::int32_t draw_aim_fov;
    std::int32_t corner_boxes;
    std::int32_t draw_names;
    std::int32_t draw_health;
    std::int32_t draw_distance;
    std::int32_t draw_weapons;
    std::int32_t draw_bomb_esp;
    std::int32_t draw_item_esp;
    std::int32_t draw_grenade_prediction;
    std::int32_t draw_snaplines;
    std::int32_t draw_offscreen_arrows;
    std::int32_t draw_radar;
    std::int32_t draw_head_marker;
    std::int32_t hide_overlay_from_capture;
    std::int32_t aim_assist_fov_pixels;
    std::int32_t aim_assist_strength_percent;
    std::int32_t aim_target_point;
    std::int32_t aim_priority;
    std::int32_t aim_movement;
    std::int32_t aim_activation;
    std::int32_t aim_activation_key;
    std::int32_t esp_theme;
    double ui_opacity;
    std::int32_t map_count;
    char version[32];
    char build[32];
    char status_title[64];
    char status_detail[256];
    char grenade_status[256];
    char diagnostics[1024];
    char config_message[256];
    char current_map[VECTRA_MENU_MAP_NAME];
    char maps[VECTRA_MENU_MAX_MAPS][VECTRA_MENU_MAP_NAME];
};

struct VectraCommandResult {
    std::int32_t success;
    char message[256];
};

using VectraGetState = void(__cdecl*)(VectraMenuState* state);
using VectraSetBool = void(__cdecl*)(std::int32_t setting, std::int32_t value);
using VectraSetInt = void(__cdecl*)(std::int32_t setting, std::int32_t value);
using VectraSetDouble = void(__cdecl*)(std::int32_t setting, double value);
using VectraSetString = void(__cdecl*)(std::int32_t setting, const char* value);
using VectraExecuteCommand = void(__cdecl*)(std::int32_t command, VectraCommandResult* result);

struct VectraMenuHostApi {
    std::int32_t api_version;
    VectraGetState get_state;
    VectraSetBool set_bool;
    VectraSetInt set_int;
    VectraSetDouble set_double;
    VectraSetString set_string;
    VectraExecuteCommand execute_command;
};

extern "C" __declspec(dllexport) std::int32_t __cdecl RunVectraMenu(const VectraMenuHostApi* api);
