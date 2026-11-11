#include <dirent.h>
#include <errno.h>
#include <stdbool.h>
#include <stdint.h>
#include <limits.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>

#include "puff.h"

#include "xsdk/gpu.h"
#include "xsdk/kernel.h"
#include "xsdk/log.h"
#include "xsdk/present.h"
#include "xsdk/splash.h"
#include "xsdk/text.h"

enum {
    FRAME_TIMEOUT_MS = 5000,
    BUFFER_COUNT = 2,
    SWAP_INTERVAL = 1,
    CLEAR_COLOR = 0xFF101820,
    WHITE = 0xFFF2F5F7,
    MUTED = 0xFF9BAAB5,
    GREEN = 0xFF7CDA42,
    SELECTED = 0xFFB8F28A,
    STORAGE_NAME_BYTES = 96,
    STORAGE_PATH_BYTES = 512,
    STORAGE_VISIBLE = 5,
    MAX_MOD_PACKAGES = 128,
    PACKAGE_VISIBLE = 6,
    MAX_DECOMPRESS_BYTES = 2 * 1024 * 1024,
};

typedef enum {
    PAGE_MENU,
    PAGE_DIAGNOSTICS,
    PAGE_ABOUT,
    PAGE_STORAGE,
    PAGE_PACKAGES,
    PAGE_PACKAGE_FILES,
} Page;

typedef struct {
    char name[STORAGE_NAME_BYTES];
    bool is_directory;
} StorageEntry;

typedef struct {
    char name[STORAGE_NAME_BYTES];
    char device[16];
    uint32_t device_index;
    unsigned long long size;
    bool structure_valid;
    char internal_name[STORAGE_NAME_BYTES];
    char mod_version[32];
    char tml_version[32];
    int32_t file_count;
    char validation[48];
} ModPackage;

static ModPackage mod_packages[MAX_MOD_PACKAGES];
static size_t mod_package_count, mod_package_selected;
static bool mod_packages_truncated;
typedef struct {
    char name[128];
    uint32_t uncompressed_size;
    uint32_t compressed_size;
    uint64_t data_offset;
} TmodEntry;
static TmodEntry *package_entries;
static size_t package_entry_count, package_entry_selected;
static bool package_entries_truncated;
static char decompress_status[64] = "A: testar descompressao em RAM";
static uint32_t decompress_actual_size;
static uint32_t decompress_expected_size;

static const char *const menu_items[] = {
    "Diagnostico do console",
    "Sobre o T360 ModLoader",
    "Armazenamento (somente leitura)",
    "Pacotes .tmod (somente leitura)",
    "Sair",
};

typedef struct { const char *label; const char *root; } StorageDevice;
static const StorageDevice storage_devices[] = {
    {"USB", "usb:\\"}, {"USB1", "usb1:\\"}, {"USB2", "usb2:\\"},
    {"HDD", "hdd:\\"}, {"MU", "mu:\\"}, {"MU1", "mu1:\\"}, {"DVD", "dvd:\\"}
};
#define DEVICE_TOTAL (sizeof(storage_devices) / sizeof(storage_devices[0]))
static uint8_t device_present[DEVICE_TOTAL];
static uint32_t device_count, device_index;
static size_t storage_count, storage_selected, storage_capacity;
static StorageEntry *storage_entries;
static int storage_error;
static char storage_path[STORAGE_PATH_BYTES];

static void copy_bounded(char *dst, size_t cap, const char *src)
{
    if (!cap) return;
    size_t n = strlen(src);
    if (n >= cap) n = cap - 1;
    memcpy(dst, src, n);
    dst[n] = '\0';
}

static int current_device_index(void)
{
    uint32_t seen = 0;
    for (uint32_t i = 0; i < DEVICE_TOTAL; ++i) {
        if (!device_present[i]) continue;
        if (seen++ == device_index) return (int)i;
    }
    return -1;
}

static void load_current_device(void)
{
    storage_count = 0;
    storage_selected = 0;
    storage_error = 0;
    int index = current_device_index();
    if (index < 0) return;
    if (!storage_path[0]) copy_bounded(storage_path, sizeof(storage_path), storage_devices[index].root);
    DIR *dir = opendir(storage_path);
    if (!dir) { storage_error = errno; return; }
    struct dirent *entry;
    while ((entry = readdir(dir)) != NULL) {
        if (!strcmp(entry->d_name, ".") || !strcmp(entry->d_name, "..")) continue;
        if (storage_count == storage_capacity) {
            size_t next_capacity = storage_capacity ? storage_capacity * 2 : 32;
            StorageEntry *grown = (StorageEntry *)realloc(storage_entries, next_capacity * sizeof(StorageEntry));
            if (!grown) { storage_error = ENOMEM; break; }
            storage_entries = grown;
            storage_capacity = next_capacity;
        }
        StorageEntry *item = &storage_entries[storage_count++];
        copy_bounded(item->name, sizeof(item->name), entry->d_name);
        item->is_directory = entry->d_type == DT_DIR;
#ifdef DT_UNKNOWN
        if (entry->d_type == DT_UNKNOWN) {
            char child[STORAGE_PATH_BYTES];
            int n = snprintf(child, sizeof(child), "%s%s\\", storage_path, entry->d_name);
            if (n > 0 && (size_t)n < sizeof(child)) {
                DIR *subdir = opendir(child);
                if (subdir) { item->is_directory = true; (void)closedir(subdir); }
            }
        }
#endif
    }
    (void)closedir(dir);
}

static void scan_storage_devices(void)
{
    memset(device_present, 0, sizeof(device_present));
    device_count = 0;
    device_index = 0;
    for (uint32_t i = 0; i < DEVICE_TOTAL; ++i) {
        DIR *dir = opendir(storage_devices[i].root);
        if (!dir) continue;
        (void)closedir(dir);
        device_present[i] = 1;
        ++device_count;
        xsdk_log_write("T360: detected %s at %s",
            storage_devices[i].label, storage_devices[i].root);
    }
    xsdk_log_write("T360: device scan finished: %u accessible device(s)", (unsigned)device_count);
    storage_path[0] = '\0';
    if (device_count) {
        int active = current_device_index();
        if (active >= 0) copy_bounded(storage_path, sizeof(storage_path), storage_devices[active].root);
    }
    load_current_device();
}


typedef struct {
    char internal_name[STORAGE_NAME_BYTES];
    char mod_version[32];
    char tml_version[32];
    int32_t file_count;
} TmodInfo;

static bool read_exact(FILE *file, void *buffer, size_t bytes)
{
    return bytes == 0 || fread(buffer, 1, bytes, file) == bytes;
}

static bool read_u32_le(FILE *file, uint32_t *value)
{
    uint8_t b[4];
    if (!read_exact(file, b, sizeof(b))) return false;
    *value = (uint32_t)b[0] | ((uint32_t)b[1] << 8) |
        ((uint32_t)b[2] << 16) | ((uint32_t)b[3] << 24);
    return true;
}

/* .NET BinaryReader strings use a 7-bit encoded byte length. */
static bool read_dotnet_string(FILE *file, char *out, size_t capacity)
{
    uint32_t length = 0;
    unsigned shift = 0;
    uint8_t byte = 0;
    for (unsigned i = 0; i < 5; ++i) {
        if (!read_exact(file, &byte, 1)) return false;
        if (i == 4 && (byte & 0xF0u)) return false;
        length |= (uint32_t)(byte & 0x7Fu) << shift;
        if (!(byte & 0x80u)) {
            if (length >= capacity || !read_exact(file, out, length)) return false;
            out[length] = '\0';
            return true;
        }
        shift += 7;
    }
    return false;
}

typedef struct {
    uint32_t state[5];
    uint64_t bit_count;
    uint8_t block[64];
    size_t used;
} Sha1Context;

static uint32_t sha1_rotate(uint32_t value, unsigned bits)
{
    return (value << bits) | (value >> (32u - bits));
}

static void sha1_transform(Sha1Context *ctx, const uint8_t block[64])
{
    uint32_t w[80];
    for (unsigned i = 0; i < 16; ++i) {
        const unsigned j = i * 4;
        w[i] = ((uint32_t)block[j] << 24) | ((uint32_t)block[j + 1] << 16) |
            ((uint32_t)block[j + 2] << 8) | (uint32_t)block[j + 3];
    }
    for (unsigned i = 16; i < 80; ++i)
        w[i] = sha1_rotate(w[i - 3] ^ w[i - 8] ^ w[i - 14] ^ w[i - 16], 1);

    uint32_t a = ctx->state[0], b = ctx->state[1], cc = ctx->state[2];
    uint32_t d = ctx->state[3], e = ctx->state[4];
    for (unsigned i = 0; i < 80; ++i) {
        uint32_t f, k;
        if (i < 20) { f = (b & cc) | ((~b) & d); k = 0x5A827999u; }
        else if (i < 40) { f = b ^ cc ^ d; k = 0x6ED9EBA1u; }
        else if (i < 60) { f = (b & cc) | (b & d) | (cc & d); k = 0x8F1BBCDCu; }
        else { f = b ^ cc ^ d; k = 0xCA62C1D6u; }
        uint32_t temp = sha1_rotate(a, 5) + f + e + k + w[i];
        e = d; d = cc; cc = sha1_rotate(b, 30); b = a; a = temp;
    }
    ctx->state[0] += a; ctx->state[1] += b; ctx->state[2] += cc;
    ctx->state[3] += d; ctx->state[4] += e;
}

static void sha1_init(Sha1Context *ctx)
{
    ctx->state[0] = 0x67452301u; ctx->state[1] = 0xEFCDAB89u;
    ctx->state[2] = 0x98BADCFEu; ctx->state[3] = 0x10325476u;
    ctx->state[4] = 0xC3D2E1F0u;
    ctx->bit_count = 0; ctx->used = 0;
}

static void sha1_update(Sha1Context *ctx, const uint8_t *data, size_t length)
{
    ctx->bit_count += (uint64_t)length * 8u;
    while (length) {
        size_t take = 64u - ctx->used;
        if (take > length) take = length;
        memcpy(ctx->block + ctx->used, data, take);
        ctx->used += take; data += take; length -= take;
        if (ctx->used == 64u) {
            sha1_transform(ctx, ctx->block);
            ctx->used = 0;
        }
    }
}

static void sha1_final(Sha1Context *ctx, uint8_t digest[20])
{
    const uint64_t bits = ctx->bit_count;
    uint8_t pad = 0x80u;
    sha1_update(ctx, &pad, 1);
    uint8_t zero = 0;
    while (ctx->used != 56u) sha1_update(ctx, &zero, 1);
    uint8_t length_bytes[8];
    for (unsigned i = 0; i < 8; ++i)
        length_bytes[7u - i] = (uint8_t)(bits >> (i * 8u));
    sha1_update(ctx, length_bytes, sizeof(length_bytes));
    for (unsigned i = 0; i < 5; ++i) {
        digest[i * 4] = (uint8_t)(ctx->state[i] >> 24);
        digest[i * 4 + 1] = (uint8_t)(ctx->state[i] >> 16);
        digest[i * 4 + 2] = (uint8_t)(ctx->state[i] >> 8);
        digest[i * 4 + 3] = (uint8_t)ctx->state[i];
    }
}

static bool verify_payload_sha1(FILE *file, long payload_start, long file_size,
    const uint8_t expected[20])
{
    if (payload_start < 0 || file_size < payload_start ||
        fseek(file, payload_start, SEEK_SET) != 0) return false;
    Sha1Context ctx;
    sha1_init(&ctx);
    uint8_t buffer[1024];
    long remaining = file_size - payload_start;
    while (remaining > 0) {
        size_t chunk = remaining > (long)sizeof(buffer) ? sizeof(buffer) : (size_t)remaining;
        if (!read_exact(file, buffer, chunk)) return false;
        sha1_update(&ctx, buffer, chunk);
        remaining -= (long)chunk;
    }
    uint8_t actual[20];
    sha1_final(&ctx, actual);
    return memcmp(actual, expected, sizeof(actual)) == 0;
}

static bool skip_exact(FILE *file, uint32_t bytes)
{
    uint8_t scratch[128];
    while (bytes) {
        size_t chunk = bytes > sizeof(scratch) ? sizeof(scratch) : bytes;
        if (!read_exact(file, scratch, chunk)) return false;
        bytes -= (uint32_t)chunk;
    }
    return true;
}

/*
 * Performs bounded structural validation and verifies the payload SHA-1.
 * The RSA mod-browser signature and runtime compatibility are not verified.
 */
static bool inspect_tmod(const char *path, TmodInfo *info, const char **reason)
{
    FILE *file = fopen(path, "rb");
    if (!file) { *reason = "Arquivo inacessivel"; return false; }

    bool valid = false;
    uint8_t magic[4];
    uint8_t expected_hash[20];
    uint32_t payload_length = 0;
    uint32_t count = 0;
    unsigned long long compressed_total = 0;
    long file_size = -1;
    long payload_start = -1;

    if (fseek(file, 0, SEEK_END) != 0 || (file_size = ftell(file)) < 0 ||
        fseek(file, 0, SEEK_SET) != 0) {
        *reason = "Falha de leitura";
        goto done;
    }
    if (!read_exact(file, magic, sizeof(magic)) ||
        memcmp(magic, "TMOD", 4) != 0) {
        *reason = "Cabecalho TMOD ausente";
        goto done;
    }
    if (!read_dotnet_string(file, info->tml_version, sizeof(info->tml_version)) ||
        !read_exact(file, expected_hash, sizeof(expected_hash)) ||
        !skip_exact(file, 256u) ||
        !read_u32_le(file, &payload_length)) {
        *reason = "Cabecalho incompleto";
        goto done;
    }

    payload_start = ftell(file);
    if (payload_start < 0 || payload_start > file_size ||
        payload_length != (uint32_t)(file_size - payload_start)) {
        *reason = "Tamanho interno inconsistente";
        goto done;
    }
    if (!verify_payload_sha1(file, payload_start, file_size, expected_hash)) {
        *reason = "Hash SHA-1 divergente";
        goto done;
    }
    if (fseek(file, payload_start, SEEK_SET) != 0) {
        *reason = "Falha de leitura";
        goto done;
    }
    if (!read_dotnet_string(file, info->internal_name, sizeof(info->internal_name)) ||
        !read_dotnet_string(file, info->mod_version, sizeof(info->mod_version)) ||
        !read_u32_le(file, &count) || count > 4096u) {
        *reason = "Metadados invalidos";
        goto done;
    }

    info->file_count = (int32_t)count;
    for (uint32_t i = 0; i < count; ++i) {
        char entry_name[512];
        uint32_t uncompressed = 0, compressed = 0;
        if (!read_dotnet_string(file, entry_name, sizeof(entry_name)) ||
            !read_u32_le(file, &uncompressed) ||
            !read_u32_le(file, &compressed)) {
            *reason = "Tabela de arquivos incompleta";
            goto done;
        }
        if (compressed > UINT32_MAX - compressed_total) {
            *reason = "Tamanho de arquivo invalido";
            goto done;
        }
        compressed_total += compressed;
        (void)uncompressed;
    }

    {
        long data_start = ftell(file);
        if (data_start < 0 || data_start > file_size ||
            compressed_total != (unsigned long long)(file_size - data_start)) {
            *reason = "Dados internos inconsistentes";
            goto done;
        }
    }
    valid = true;
    *reason = "Estrutura reconhecida";

done:
    fclose(file);
    return valid;
}

static void validate_mod_package(ModPackage *item, uint32_t device_index_value)
{
    char path[STORAGE_PATH_BYTES];
    int n = snprintf(path, sizeof(path), "%sT360\\\\Mods\\\\%s",
        storage_devices[device_index_value].root, item->name);
    if (n <= 0 || (size_t)n >= sizeof(path)) {
        copy_bounded(item->validation, sizeof(item->validation), "Caminho muito longo");
        return;
    }
    TmodInfo info = { 0 };
    const char *reason = "Pacote invalido";
    item->structure_valid = inspect_tmod(path, &info, &reason);
    copy_bounded(item->validation, sizeof(item->validation), reason);
    if (item->structure_valid) {
        copy_bounded(item->internal_name, sizeof(item->internal_name), info.internal_name);
        copy_bounded(item->mod_version, sizeof(item->mod_version), info.mod_version);
        copy_bounded(item->tml_version, sizeof(item->tml_version), info.tml_version);
        item->file_count = info.file_count;
    }
}

static void clear_package_entries(void)
{
    free(package_entries);
    package_entries = NULL;
    package_entry_count = 0;
    package_entry_selected = 0;
    package_entries_truncated = false;
}

static bool load_package_entries(const ModPackage *item)
{
    clear_package_entries();
    char path[STORAGE_PATH_BYTES];
    int n = snprintf(path, sizeof(path), "%sT360\\Mods\\%s",
        storage_devices[item->device_index].root, item->name);
    if (n <= 0 || (size_t)n >= sizeof(path)) return false;
    FILE *file = fopen(path, "rb");
    if (!file) return false;
    uint8_t magic[4];
    char tml_version[32], mod_name[STORAGE_NAME_BYTES], mod_version[32];
    uint32_t payload_length = 0, count = 0;
    bool ok = read_exact(file, magic, 4) && memcmp(magic, "TMOD", 4) == 0 &&
        read_dotnet_string(file, tml_version, sizeof(tml_version)) &&
        skip_exact(file, 20u + 256u) && read_u32_le(file, &payload_length) &&
        read_dotnet_string(file, mod_name, sizeof(mod_name)) &&
        read_dotnet_string(file, mod_version, sizeof(mod_version)) &&
        read_u32_le(file, &count) && count <= 4096u;
    if (!ok) { fclose(file); return false; }
    package_entries = (TmodEntry *)calloc(count ? count : 1, sizeof(TmodEntry));
    if (!package_entries) { fclose(file); return false; }
    for (uint32_t i = 0; i < count; ++i) {
        char entry_name[512];
        uint32_t raw_size = 0, packed_size = 0;
        if (!read_dotnet_string(file, entry_name, sizeof(entry_name)) ||
            !read_u32_le(file, &raw_size) || !read_u32_le(file, &packed_size)) {
            clear_package_entries(); fclose(file); return false;
        }
        copy_bounded(package_entries[i].name, sizeof(package_entries[i].name), entry_name);
        package_entries[i].uncompressed_size = raw_size;
        package_entries[i].compressed_size = packed_size;
        ++package_entry_count;
    }
    package_entries_truncated = false;
    {
        long data_start = ftell(file);
        if (data_start < 0) { clear_package_entries(); fclose(file); return false; }
        uint64_t offset = (uint64_t)data_start;
        for (size_t i = 0; i < package_entry_count; ++i) {
            package_entries[i].data_offset = offset;
            offset += package_entries[i].compressed_size;
            if (offset > (uint64_t)LONG_MAX) {
                clear_package_entries(); fclose(file); return false;
            }
        }
        if (fseek(file, 0, SEEK_END) != 0) {
            clear_package_entries(); fclose(file); return false;
        }
        long end = ftell(file);
        if (end < 0 || offset != (uint64_t)end) {
            clear_package_entries(); fclose(file); return false;
        }
    }
    copy_bounded(decompress_status, sizeof(decompress_status), "A: testar descompressao em RAM");
    decompress_actual_size = 0;
    decompress_expected_size = 0;
    fclose(file);
    return true;
}

static void test_selected_decompression(void)
{
    if (package_entry_selected >= package_entry_count) return;
    const TmodEntry *entry = &package_entries[package_entry_selected];
    decompress_actual_size = 0;
    decompress_expected_size = entry->uncompressed_size;
    if (entry->uncompressed_size > MAX_DECOMPRESS_BYTES ||
        entry->compressed_size > MAX_DECOMPRESS_BYTES) {
        copy_bounded(decompress_status, sizeof(decompress_status), "ARQUIVO ACIMA DO LIMITE (2 MiB)");
        return;
    }
    char path[STORAGE_PATH_BYTES];
    const ModPackage *pkg = &mod_packages[mod_package_selected];
    int n = snprintf(path, sizeof(path), "%sT360\\Mods\\%s",
        storage_devices[pkg->device_index].root, pkg->name);
    if (n <= 0 || (size_t)n >= sizeof(path)) {
        copy_bounded(decompress_status, sizeof(decompress_status), "CAMINHO INVALIDO");
        return;
    }
    FILE *file = fopen(path, "rb");
    if (!file) {
        copy_bounded(decompress_status, sizeof(decompress_status), "FALHA AO ABRIR PACOTE");
        return;
    }
    if (entry->data_offset > (uint64_t)LONG_MAX ||
        fseek(file, (long)entry->data_offset, SEEK_SET) != 0) {
        fclose(file);
        copy_bounded(decompress_status, sizeof(decompress_status), "FALHA AO POSICIONAR LEITURA");
        return;
    }
    unsigned char *input = (unsigned char *)malloc(entry->compressed_size ? entry->compressed_size : 1);
    unsigned char *output = (unsigned char *)malloc(entry->uncompressed_size ? entry->uncompressed_size : 1);
    if (!input || !output) {
        free(input); free(output); fclose(file);
        copy_bounded(decompress_status, sizeof(decompress_status), "MEMORIA INSUFICIENTE");
        return;
    }
    if (!read_exact(file, input, entry->compressed_size)) {
        free(input); free(output); fclose(file);
        copy_bounded(decompress_status, sizeof(decompress_status), "LEITURA INCOMPLETA");
        return;
    }
    fclose(file);
    if (entry->compressed_size == entry->uncompressed_size) {
        if (entry->uncompressed_size)
            memcpy(output, input, entry->uncompressed_size);
        decompress_actual_size = entry->uncompressed_size;
    } else {
        unsigned long out_len = entry->uncompressed_size;
        unsigned long in_len = entry->compressed_size;
        int result = puff(output, &out_len, input, &in_len);
        if (result != 0) {
            free(input); free(output);
            copy_bounded(decompress_status, sizeof(decompress_status), "DEFLATE INVALIDO");
            return;
        }
        decompress_actual_size = (uint32_t)out_len;
    }
    free(input);
    free(output);
    if (decompress_actual_size != decompress_expected_size) {
        copy_bounded(decompress_status, sizeof(decompress_status), "TAMANHO DESCOMPACTADO DIFERENTE");
        return;
    }
    copy_bounded(decompress_status, sizeof(decompress_status), "DESCOMPACTADO OK (RAM; NAO SALVO)");
}

static bool has_tmod_extension(const char *name)
{
    const char *dot = strrchr(name, '.');
    if (!dot || strlen(dot) != 5) return false;
    return (dot[1] == 't' || dot[1] == 'T') &&
        (dot[2] == 'm' || dot[2] == 'M') &&
        (dot[3] == 'o' || dot[3] == 'O') &&
        (dot[4] == 'd' || dot[4] == 'D');
}

static void scan_mod_packages(void)
{
    mod_package_count = 0;
    mod_package_selected = 0;
    mod_packages_truncated = false;
    for (uint32_t i = 0; i < DEVICE_TOTAL; ++i) {
        if (!device_present[i]) continue;
        char folder[STORAGE_PATH_BYTES];
        int n = snprintf(folder, sizeof(folder), "%sT360\\Mods\\", storage_devices[i].root);
        if (n <= 0 || (size_t)n >= sizeof(folder)) continue;
        DIR *dir = opendir(folder);
        if (!dir) continue;
        struct dirent *entry;
        while ((entry = readdir(dir)) != NULL) {
            if (!has_tmod_extension(entry->d_name)) continue;
            if (mod_package_count >= MAX_MOD_PACKAGES) {
                mod_packages_truncated = true;
                break;
            }
            char full_path[STORAGE_PATH_BYTES];
            n = snprintf(full_path, sizeof(full_path), "%s%s", folder, entry->d_name);
            if (n <= 0 || (size_t)n >= sizeof(full_path)) continue;
            struct stat info;
            if (stat(full_path, &info) != 0 || S_ISDIR(info.st_mode)) continue;
            ModPackage *item = &mod_packages[mod_package_count++];
            copy_bounded(item->name, sizeof(item->name), entry->d_name);
            copy_bounded(item->device, sizeof(item->device), storage_devices[i].label);
            item->device_index = i;
            item->size = (unsigned long long)info.st_size;
            validate_mod_package(item, i);
        }
        (void)closedir(dir);
        if (mod_packages_truncated) break;
    }
    xsdk_log_write("T360: found %u .tmod candidate(s)", (unsigned)mod_package_count);
}

static void select_device(int direction)
{
    if (device_count < 2) return;
    if (direction > 0) device_index = (device_index + 1) % device_count;
    else device_index = device_index == 0 ? device_count - 1 : device_index - 1;
    int active = current_device_index();
    storage_path[0] = '\0';
    if (active >= 0) copy_bounded(storage_path, sizeof(storage_path), storage_devices[active].root);
    load_current_device();
}

static bool storage_enter_directory(void)
{
    if (storage_selected >= storage_count || !storage_entries[storage_selected].is_directory) return false;
    size_t used = strlen(storage_path);
    int n = snprintf(storage_path + used, sizeof(storage_path) - used, "%s\\", storage_entries[storage_selected].name);
    if (n <= 0 || (size_t)n >= sizeof(storage_path) - used) return false;
    load_current_device();
    return true;
}

static bool storage_parent_directory(void)
{
    int active = current_device_index();
    if (active < 0) return false;
    const char *root = storage_devices[active].root;
    size_t root_len = strlen(root);
    size_t len = strlen(storage_path);
    if (len <= root_len) return false;
    while (len > root_len && (storage_path[len - 1] == '\\' || storage_path[len - 1] == '/'))
        storage_path[--len] = '\0';
    char *last = strrchr(storage_path, '\\');
    if (!last) last = strrchr(storage_path, '/');
    if (!last || (size_t)(last - storage_path) < root_len - 1)
        copy_bounded(storage_path, sizeof(storage_path), root);
    else
        last[1] = '\0';
    load_current_device();
    return true;
}

static void draw_line(int32_t x, int32_t y, uint32_t color, const char *text)
{
    xsdk_text_draw(x, y, 2, color, text);
}


static void draw_file_details(void)
{
    xsdk_text_draw(760, 372, 1, SELECTED, "DETALHES DO ITEM");
    if (storage_selected >= storage_count) {
        xsdk_text_draw(760, 410, 1, MUTED, "Nenhum item selecionado.");
        return;
    }
    const StorageEntry *item = &storage_entries[storage_selected];
    char full_path[STORAGE_PATH_BYTES];
    int n = snprintf(full_path, sizeof(full_path), "%s%s", storage_path, item->name);
    char line[160];
    xsdk_text_draw(760, 410, 1, WHITE, item->name);
    const char *dot = strrchr(item->name, '.');
    (void)snprintf(line, sizeof(line), "Extensao: %s",
        (!item->is_directory && dot && dot[1]) ? dot + 1 : (item->is_directory ? "—" : "Sem extensao"));
    xsdk_text_draw(760, 444, 1, MUTED, line);
    xsdk_text_draw(760, 478, 1, MUTED,
        item->is_directory ? "Tipo: Pasta" : "Tipo: Arquivo");
    if (item->is_directory) {
        xsdk_text_draw(760, 512, 1, MUTED, "Tamanho: —");
    } else if (n > 0 && (size_t)n < sizeof(full_path)) {
        struct stat info;
        if (stat(full_path, &info) == 0) {
            (void)snprintf(line, sizeof(line), "Tamanho: %llu bytes",
                (unsigned long long)info.st_size);
            xsdk_text_draw(760, 512, 1, MUTED, line);
        } else {
            xsdk_text_draw(760, 512, 1, MUTED, "Tamanho: indisponivel");
        }
    } else {
        xsdk_text_draw(760, 512, 1, MUTED, "Tamanho: caminho longo");
    }
    xsdk_text_draw(760, 552, 1, MUTED, "A abre pastas; arquivos nao sao alterados.");
}

static void draw_storage(uint32_t height)
{
    draw_line(80, 238, SELECTED, "EXPLORADOR DE ARMAZENAMENTO");
    xsdk_text_draw(80, 278, 1, MUTED, "Somente leitura  |  ESQ/DIR troca a unidade");
    if (!device_count) {
        xsdk_text_draw(80, 360, 2, WHITE, "Nenhuma unidade acessivel detectada.");
        xsdk_text_draw(80, 404, 1, MUTED, "Conecte um dispositivo e pressione A para atualizar.");
    } else {
        int active = current_device_index();
        if (active >= 0) {
            xsdk_text_draw(80, 320, 2, SELECTED, storage_devices[active].label);
            xsdk_text_draw(210, 324, 1, MUTED, storage_path);
        }
        xsdk_text_draw(80, 370, 1, MUTED, "UNIDADES");
        int32_t y = 400;
        for (uint32_t i = 0; i < DEVICE_TOTAL; ++i) {
            if (!device_present[i]) continue;
            char label[32];
            (void)snprintf(label, sizeof(label), "%c %s",
                active == (int)i ? '>' : ' ', storage_devices[i].label);
            xsdk_text_draw(88, y, 1, active == (int)i ? SELECTED : WHITE, label);
            y += 28;
        }
        xsdk_text_draw(330, 370, 1, MUTED, "CONTEUDO DA PASTA");
        if (storage_error) {
            xsdk_text_draw(330, 410, 1, WHITE, "Falha ao ler pasta.");
        } else if (!storage_count) {
            xsdk_text_draw(330, 410, 1, WHITE, "Pasta vazia.");
        } else {
            size_t first = storage_selected >= STORAGE_VISIBLE ? storage_selected - STORAGE_VISIBLE + 1 : 0;
            size_t end = first + STORAGE_VISIBLE;
            if (end > storage_count) end = storage_count;
            for (size_t i = first; i < end; ++i) {
                char line[STORAGE_NAME_BYTES + 8];
                (void)snprintf(line, sizeof(line), "%c %s%s",
                    i == storage_selected ? '>' : ' ',
                    storage_entries[i].name,
                    storage_entries[i].is_directory ? "/" : "");
                xsdk_text_draw(340, 408 + (int32_t)(i - first) * 29, 1,
                    i == storage_selected ? SELECTED : WHITE, line);
            }
            char counter[48];
            (void)snprintf(counter, sizeof(counter), "Item %u de %u",
                (unsigned)(storage_selected + 1), (unsigned)storage_count);
            xsdk_text_draw(340, 558, 1, MUTED, counter);
            draw_file_details();
        }
    }
    xsdk_text_draw(72, (int32_t)height - 72, 1, MUTED,
        "CIMA/BAIXO: navegar  A: abrir pasta  B: voltar  ESQ/DIR: unidade");
}

static void draw_package_files(uint32_t height)
{
    draw_line(80, 238, SELECTED, "ARQUIVOS INTERNOS DO PACOTE");
    xsdk_text_draw(80, 278, 1, MUTED, "A descompacta o arquivo selecionado apenas na RAM");
    if (!package_entry_count) {
        xsdk_text_draw(80, 360, 2, WHITE, "Nenhum arquivo listado.");
    } else {
        size_t first = package_entry_selected >= PACKAGE_VISIBLE ? package_entry_selected - PACKAGE_VISIBLE + 1 : 0;
        size_t end = first + PACKAGE_VISIBLE;
        if (end > package_entry_count) end = package_entry_count;
        for (size_t i = first; i < end; ++i) {
            char line[160];
            (void)snprintf(line, sizeof(line), "%c %s",
                i == package_entry_selected ? '>' : ' ', package_entries[i].name);
            xsdk_text_draw(96, 340 + (int32_t)(i - first) * 38, 1,
                i == package_entry_selected ? SELECTED : WHITE, line);
        }
        char detail[128];
        const TmodEntry *entry = &package_entries[package_entry_selected];
        (void)snprintf(detail, sizeof(detail), "Descompactado: %u bytes", (unsigned)entry->uncompressed_size);
        xsdk_text_draw(760, 382, 1, WHITE, detail);
        (void)snprintf(detail, sizeof(detail), "Compactado: %u bytes", (unsigned)entry->compressed_size);
        xsdk_text_draw(760, 420, 1, WHITE, detail);
        (void)snprintf(detail, sizeof(detail), "Arquivo %u de %u",
            (unsigned)(package_entry_selected + 1), (unsigned)package_entry_count);
        xsdk_text_draw(760, 462, 1, MUTED, detail);
        xsdk_text_draw(760, 510, 1, SELECTED, decompress_status);
        if (decompress_expected_size) {
            (void)snprintf(detail, sizeof(detail), "Resultado: %u / %u bytes",
                (unsigned)decompress_actual_size, (unsigned)decompress_expected_size);
            xsdk_text_draw(760, 544, 1, MUTED, detail);
        }
    }
    if (package_entries_truncated)
        xsdk_text_draw(760, 500, 1, MUTED, "Lista interna limitada.");
    xsdk_text_draw(72, (int32_t)height - 72, 1, MUTED,
        "CIMA/BAIXO: navegar  A: descompactar em RAM  B: voltar");
}

static void draw_packages(uint32_t height)
{
    draw_line(80, 238, SELECTED, "PACOTES LOCAIS .TMOD");
    xsdk_text_draw(80, 278, 1, MUTED, "Busca somente leitura em T360\\Mods\\ nas unidades acessiveis");
    if (!mod_package_count) {
        xsdk_text_draw(80, 360, 2, WHITE, "Nenhum pacote .tmod encontrado.");
        xsdk_text_draw(80, 408, 1, MUTED, "Pasta esperada: T360\\Mods\\ na raiz do USB ou HDD.");
        xsdk_text_draw(80, 444, 1, MUTED, "Esta etapa apenas detecta arquivos; nao valida nem instala.");
    } else {
        size_t first = mod_package_selected >= PACKAGE_VISIBLE ?
            mod_package_selected - PACKAGE_VISIBLE + 1 : 0;
        size_t end = first + PACKAGE_VISIBLE;
        if (end > mod_package_count) end = mod_package_count;
        for (size_t i = first; i < end; ++i) {
            char line[STORAGE_NAME_BYTES + 24];
            (void)snprintf(line, sizeof(line), "%c %s",
                i == mod_package_selected ? '>' : ' ', mod_packages[i].name);
            xsdk_text_draw(96, 340 + (int32_t)(i - first) * 38, 1,
                i == mod_package_selected ? SELECTED : WHITE, line);
        }
        const ModPackage *item = &mod_packages[mod_package_selected];
        char detail[160];
        xsdk_text_draw(760, 340, 1, SELECTED, "PACOTE DETECTADO");
        (void)snprintf(detail, sizeof(detail), "Unidade: %s", item->device);
        xsdk_text_draw(760, 382, 1, WHITE, detail);
        (void)snprintf(detail, sizeof(detail), "Tamanho: %llu bytes", item->size);
        xsdk_text_draw(760, 420, 1, WHITE, detail);
        xsdk_text_draw(760, 462, 1, item->structure_valid ? GREEN : MUTED, item->validation);
        if (item->structure_valid) {
            (void)snprintf(detail, sizeof(detail), "Mod: %s", item->internal_name);
            xsdk_text_draw(760, 496, 1, WHITE, detail);
            (void)snprintf(detail, sizeof(detail), "Versao: %s", item->mod_version);
            xsdk_text_draw(760, 528, 1, WHITE, detail);
            (void)snprintf(detail, sizeof(detail), "Arquivos: %ld", (long)item->file_count);
            xsdk_text_draw(760, 560, 1, WHITE, detail);
            (void)snprintf(detail, sizeof(detail), "tModLoader: %s", item->tml_version);
            xsdk_text_draw(760, 592, 1, MUTED, detail);
        }
        xsdk_text_draw(760, 624, 1, MUTED, "SHA-1 OK; assinatura RSA nao verificada");
        xsdk_text_draw(760, 660, 1, MUTED, "A: ver indice interno");
        char counter[48];
        (void)snprintf(counter, sizeof(counter), "Pacote %u de %u",
            (unsigned)(mod_package_selected + 1), (unsigned)mod_package_count);
        xsdk_text_draw(96, 590, 1, MUTED, counter);
    }
    if (mod_packages_truncated)
        xsdk_text_draw(760, 550, 1, MUTED, "Lista limitada a 128 pacotes.");
    xsdk_text_draw(72, (int32_t)height - 72, 1, MUTED,
        "CIMA/BAIXO: navegar  B: voltar  |  Somente leitura");
}

static void draw_screen(uint32_t width, uint32_t height, Page page, uint32_t selected,
    uint32_t controller_status)
{
    uint32_t address = 0;
    const NTSTATUS status = xsdk_present_begin(&address, FRAME_TIMEOUT_MS);
    if (!NT_SUCCESS(status)) {
        xsdk_log_write("T360: xsdk_present_begin failed: 0x%08X", (unsigned)status);
        return;
    }

    xsdk_gpu_set_target(width, height);
    draw_line(72, 48, GREEN, "T360 MODLOADER");
    xsdk_text_draw(72, 94, 1, MUTED, "TERRARIA XBOX 360  |  GERENCIADOR NATIVO RGH");
    xsdk_text_draw(72, 144, 1, MUTED, "BUILD EXPERIMENTAL  •  LEITURA SEGURA");

    if (page == PAGE_MENU) {
        draw_line(80, 232, SELECTED, "CENTRAL DO PROJETO");
        xsdk_text_draw(80, 278, 1, MUTED, "Selecione uma ferramenta");
        for (uint32_t i = 0; i < sizeof menu_items / sizeof menu_items[0]; ++i) {
            const uint32_t color = i == selected ? SELECTED : WHITE;
            char line[112];
            (void)snprintf(line, sizeof(line), "%c  %s",
                i == selected ? '>' : ' ', menu_items[i]);
            xsdk_text_draw(104, 334 + (int32_t)i * 54, 2, color, line);
        }
        xsdk_text_draw(760, 278, 1, SELECTED, "ESTADO DO SISTEMA");
        xsdk_text_draw(760, 322, 1, WHITE, "Interface: operacional");
        xsdk_text_draw(760, 360, 1, WHITE, "Armazenamento: explorador");
        xsdk_text_draw(760, 398, 1, MUTED, "Mods: carregamento ainda nao");
        xsdk_text_draw(760, 424, 1, MUTED, "implementado");
        xsdk_text_draw(760, 490, 1, MUTED, "Nenhum arquivo e modificado");
        xsdk_text_draw(72, (int32_t)height - 72, 1, MUTED,
            "D-PAD: navegar   A: abrir   BACK: sair");
    } else if (page == PAGE_DIAGNOSTICS) {
        draw_line(92, 250, SELECTED, "DIAGNOSTICO");
        xsdk_text_draw(92, 316, 2, WHITE,
            controller_status == ERROR_SUCCESS ? "Controle: conectado" : "Controle: nao detectado");
        xsdk_text_draw(92, 370, 2, WHITE, "Video: modo 720p inicializado");
        xsdk_text_draw(92, 424, 2, WHITE, "Interface: renderizacao ativa");
        xsdk_text_draw(92, 500, 1, MUTED, "Este teste nao altera arquivos do console.");
        xsdk_text_draw(72, (int32_t)height - 72, 1, MUTED, "B ou BACK: voltar");
    } else if (page == PAGE_ABOUT) {
        draw_line(92, 250, SELECTED, "SOBRE");
        xsdk_text_draw(92, 316, 2, WHITE, "T360 ModLoader");
        xsdk_text_draw(92, 370, 2, WHITE, "Projeto comunitario para Terraria Xbox 360.");
        xsdk_text_draw(92, 424, 2, WHITE, "O carregamento de mods ainda nao esta implementado.");
        xsdk_text_draw(92, 478, 2, WHITE, "Nenhum arquivo do Terraria e modificado.");
        xsdk_text_draw(72, (int32_t)height - 72, 1, MUTED, "B ou BACK: voltar");
    } else if (page == PAGE_STORAGE) {
        draw_storage(height);
    } else if (page == PAGE_PACKAGE_FILES) {
        draw_package_files(height);
    } else {
        draw_packages(height);
    }

    xsdk_gpu_resolve(address, width, height, CLEAR_COLOR);
    xsdk_present_end();
}

int main(void)
{
    xsdk_log_write("T360: native manager startup");
    const NTSTATUS splash = xsdk_splash_show(1200);
    if (!NT_SUCCESS(splash))
        xsdk_log_write("T360: splash unavailable: 0x%08X", (unsigned)splash);

    NTSTATUS status = xsdk_gpu_open();
    if (!NT_SUCCESS(status)) {
        xsdk_log_write("T360: GPU initialization failed: 0x%08X", (unsigned)status);
        return 1;
    }

    uint32_t width = 0;
    uint32_t height = 0;
    status = xsdk_present_open(XSDK_DISPLAY_720P, BUFFER_COUNT, SWAP_INTERVAL, &width, &height);
    if (!NT_SUCCESS(status)) {
        xsdk_log_write("T360: display initialization failed: 0x%08X", (unsigned)status);
        xsdk_gpu_close();
        return 1;
    }

    status = xsdk_text_open();
    if (!NT_SUCCESS(status)) {
        xsdk_log_write("T360: text initialization failed: 0x%08X", (unsigned)status);
        xsdk_present_close();
        xsdk_gpu_close();
        return 1;
    }

    xsdk_log_write("T360: display ready: %ux%u", (unsigned)width, (unsigned)height);

    Page page = PAGE_MENU;
    uint32_t selected = 0;
    uint16_t previous_buttons = 0;
    bool running = true;

    while (running) {
        INPUT_STATE input = { 0 };
        const uint32_t controller_status = XamInputGetState(0, 0, &input);
        if (controller_status != ERROR_SUCCESS)
            input = (INPUT_STATE){ 0 };

        const uint16_t buttons = input.gamepad.buttons;
        const uint16_t pressed = buttons & (uint16_t)~previous_buttons;
        previous_buttons = buttons;

        if (pressed & INPUT_GAMEPAD_BACK) {
            if (page == PAGE_MENU)
                running = false;
            else
                page = PAGE_MENU;
        } else if (page == PAGE_MENU) {
            if (pressed & INPUT_GAMEPAD_DPAD_UP)
                selected = selected == 0 ? 4 : selected - 1;
            if (pressed & INPUT_GAMEPAD_DPAD_DOWN)
                selected = (selected + 1) % 5;
            if (pressed & INPUT_GAMEPAD_A) {
                if (selected == 0)
                    page = PAGE_DIAGNOSTICS;
                else if (selected == 1)
                    page = PAGE_ABOUT;
                else if (selected == 2) {
                    page = PAGE_STORAGE;
                    scan_storage_devices();
                } else if (selected == 3) {
                    page = PAGE_PACKAGES;
                    scan_storage_devices();
                    scan_mod_packages();
                } else {
                    running = false;
                }
            }
        } else if (page == PAGE_PACKAGE_FILES) {
            if (package_entry_count > 0 && (pressed & INPUT_GAMEPAD_DPAD_UP))
                package_entry_selected = package_entry_selected == 0 ? package_entry_count - 1 : package_entry_selected - 1;
            if (package_entry_count > 0 && (pressed & INPUT_GAMEPAD_DPAD_DOWN))
                package_entry_selected = (package_entry_selected + 1) % package_entry_count;
            if (pressed & INPUT_GAMEPAD_A) test_selected_decompression();
            if (pressed & INPUT_GAMEPAD_B) page = PAGE_PACKAGES;
        } else if (page == PAGE_PACKAGES) {
            if (mod_package_count > 0 && (pressed & INPUT_GAMEPAD_DPAD_UP))
                mod_package_selected = mod_package_selected == 0 ? mod_package_count - 1 : mod_package_selected - 1;
            if (mod_package_count > 0 && (pressed & INPUT_GAMEPAD_DPAD_DOWN))
                mod_package_selected = (mod_package_selected + 1) % mod_package_count;
            if (mod_package_count > 0 && (pressed & INPUT_GAMEPAD_A)) {
                if (load_package_entries(&mod_packages[mod_package_selected])) page = PAGE_PACKAGE_FILES;
            }
        } else if (page == PAGE_STORAGE) {
            if (pressed & INPUT_GAMEPAD_DPAD_LEFT) select_device(-1);
            if (pressed & INPUT_GAMEPAD_DPAD_RIGHT) select_device(1);
            if (storage_count > 0 && (pressed & INPUT_GAMEPAD_DPAD_UP))
                storage_selected = storage_selected == 0 ? storage_count - 1 : storage_selected - 1;
            if (storage_count > 0 && (pressed & INPUT_GAMEPAD_DPAD_DOWN))
                storage_selected = (storage_selected + 1) % storage_count;
            if (pressed & INPUT_GAMEPAD_A) {
                if (!storage_enter_directory() && storage_count == 0) scan_storage_devices();
            }
            if (pressed & INPUT_GAMEPAD_B) {
                if (!storage_parent_directory()) page = PAGE_MENU;
            }
        } else if (pressed & INPUT_GAMEPAD_B) {
            page = PAGE_MENU;
        }

        draw_screen(width, height, page, selected, controller_status);
    }

    xsdk_text_close();
    xsdk_present_close();
    status = xsdk_gpu_close();
    if (!NT_SUCCESS(status))
        xsdk_log_write("T360: GPU shutdown warning: 0x%08X", (unsigned)status);
    xsdk_log_write("T360: clean exit");
    return 0;
}
