#!/usr/bin/env python3
"""Apply the T360-verified page-hash rebuild extension to a checked-out XexTool-RE tree."""
from pathlib import Path

root = Path(__file__).resolve().parents[2] / "tools" / "XexTool-RE"
convert = root / "src" / "convert.cpp"
header = root / "src" / "convert.h"
main = root / "src" / "main.cpp"

src = convert.read_text(encoding="utf-8")
if "T360_PAGE_HASH_CHAIN_REBUILDER" in src:
    print("XexTool-RE page-hash extension already applied")
else:
    marker = "namespace xex {\nnamespace {\n"
    if src.count(marker) != 1:
        raise SystemExit("convert.cpp anonymous namespace anchor changed; refusing patch")
    helper = r"""
// T360_PAGE_HASH_CHAIN_REBUILDER
// XEX page descriptors form a backwards SHA-1 chain. Each digest covers the
// corresponding page range (zero-padded to its declared page count) followed
// by that descriptor's 24 bytes. image-info.ImageHash roots descriptor zero.
bool rebuild_page_hash_chain(std::vector<uint8_t>& header, uint32_t sec,
                             const std::vector<uint8_t>& image,
                             uint32_t image_flags, uint32_t descriptor_count) {
    const uint32_t page_size = (image_flags & 0x10000000u) ? 0x1000u : 0x10000u;
    const size_t descriptors = size_t(sec) + 0x184;
    if (descriptor_count == 0 || descriptors + size_t(descriptor_count) * 24 > header.size())
        return false;

    std::vector<uint32_t> page_counts(descriptor_count);
    size_t total_declared = 0;
    for (uint32_t i = 0; i < descriptor_count; ++i) {
        const size_t off = descriptors + size_t(i) * 24;
        const uint32_t info = (uint32_t(header[off]) << 24) |
            (uint32_t(header[off+1]) << 16) | (uint32_t(header[off+2]) << 8) | header[off+3];
        page_counts[i] = info >> 4;
        if (page_counts[i] == 0) return false;
        total_declared += size_t(page_counts[i]) * page_size;
    }
    if (total_declared < image.size()) return false;

    // The terminal descriptor's outgoing digest is unused; canonicalize it to zero.
    const size_t last = descriptors + size_t(descriptor_count - 1) * 24;
    std::memset(&header[last + 4], 0, 20);

    std::vector<size_t> starts(descriptor_count);
    size_t cursor = 0;
    for (uint32_t i = 0; i < descriptor_count; ++i) {
        starts[i] = cursor;
        cursor += size_t(page_counts[i]) * page_size;
    }

    for (uint32_t i = descriptor_count - 1; i > 0; --i) {
        const size_t desc_off = descriptors + size_t(i) * 24;
        const size_t declared = size_t(page_counts[i]) * page_size;
        const size_t begin = starts[i];
        const size_t available = begin < image.size() ? std::min(declared, image.size() - begin) : 0;
        std::vector<uint8_t> material;
        material.reserve(declared + 24);
        if (available) material.insert(material.end(), image.begin() + begin, image.begin() + begin + available);
        material.resize(declared, 0);
        material.insert(material.end(), header.begin() + desc_off, header.begin() + desc_off + 24);
        const auto digest = crypto::Sha1::hash(material.data(), material.size());
        const size_t previous = descriptors + size_t(i - 1) * 24;
        std::memcpy(&header[previous + 4], digest.data(), 20);
    }

    const size_t first_off = descriptors;
    const size_t first_declared = size_t(page_counts[0]) * page_size;
    const size_t first_available = std::min(first_declared, image.size());
    std::vector<uint8_t> first;
    first.reserve(first_declared + 24);
    first.insert(first.end(), image.begin(), image.begin() + first_available);
    first.resize(first_declared, 0);
    first.insert(first.end(), header.begin() + first_off, header.begin() + first_off + 24);
    const auto root = crypto::Sha1::hash(first.data(), first.size());
    std::memcpy(&header[size_t(sec) + 0x114], root.data(), 20);
    return true;
}

bool verify_page_hash_chain(const std::vector<uint8_t>& header, uint32_t sec,
                            const std::vector<uint8_t>& image,
                            uint32_t image_flags, uint32_t descriptor_count) {
    auto rebuilt = header;
    if (!rebuild_page_hash_chain(rebuilt, sec, image, image_flags, descriptor_count))
        return false;
    const size_t descriptors = size_t(sec) + 0x184;
    const size_t bytes = size_t(descriptor_count) * 24;
    return std::equal(rebuilt.begin() + descriptors, rebuilt.begin() + descriptors + bytes,
                      header.begin() + descriptors) &&
           std::equal(rebuilt.begin() + size_t(sec) + 0x114,
                      rebuilt.begin() + size_t(sec) + 0x128,
                      header.begin() + size_t(sec) + 0x114);
}
"""
    src = src.replace(marker, marker + helper, 1)

    # Both conversion paths must rebuild the descriptor chain before signing the header.
    sig = "crypto::devkit_sign(B.hdr, B.data_offset,"
    if src.count(sig) != 2:
        raise SystemExit(f"expected two conversion signing points, found {src.count(sig)}")
    src = src.replace(sig, """if (!rebuild_page_hash_chain(B.hdr, B.sec, image,
                                x.security().image_flags, x.security().page_count))
        return x.raw();
    crypto::devkit_sign(B.hdr, B.data_offset,")

    # Add public verification API outside the anonymous namespace.
    end_marker = """std::vector<uint8_t> reserialize_with_image(const XexFile& x,
                                            const std::vector<uint8_t>& image) {
    return x.is_compressed() ? compress_to_normal(x, x.is_encrypted(), &image)
                             : decompress_to_basic(x, false, &image);
}

} // namespace xex"""
    if src.count(end_marker) != 1:
        raise SystemExit("convert.cpp public API insertion anchor changed")
    public_api = """std::vector<uint8_t> reserialize_with_image(const XexFile& x,
                                            const std::vector<uint8_t>& image) {
    return x.is_compressed() ? compress_to_normal(x, x.is_encrypted(), &image)
                             : decompress_to_basic(x, false, &image);
}

bool verify_page_hash_chain(const XexFile& x, const std::vector<uint8_t>& image) {
    const auto& raw = x.raw();
    std::vector<uint8_t> header(raw.begin(), raw.begin() + x.data_offset());
    return verify_page_hash_chain(header, x.sec_info_offset(), image,
                                  x.security().image_flags, x.security().page_count);
}

} // namespace xex"""
    src = src.replace(end_marker, public_api)
    convert.write_text(src, encoding="utf-8")

hdr = header.read_text(encoding="utf-8")
if "bool verify_page_hash_chain(const XexFile& x" not in hdr:
    anchor = """std::vector<uint8_t> reserialize_with_image(const XexFile& x,
                                            const std::vector<uint8_t>& image);
"""
    if hdr.count(anchor) != 1:
        raise SystemExit("convert.h API insertion anchor changed")
    hdr = hdr.replace(anchor, anchor + """
// Verify the XEX page-descriptor SHA-1 chain against a decompressed basefile.
bool verify_page_hash_chain(const XexFile& x, const std::vector<uint8_t>& image);
""")
    header.write_text(hdr, encoding="utf-8")

m = main.read_text(encoding="utf-8")
if 'cmd == "replace-basefile"' not in m:
    anchor = """    if (argc < 3) { usage(); return argc < 2 ? 1 : 0; }
"""
    if m.count(anchor) != 1:
        raise SystemExit("main.cpp command insertion anchor changed")
    command = r"""
    if (argc >= 2 && std::string(argv[1]) == "replace-basefile") {
        if (argc < 5) {
            std::fprintf(stderr, "usage: replace-basefile <input.xex> <modified-basefile> <output.xex>\n");
            return 1;
        }
        std::vector<uint8_t> xex_bytes, image;
        if (!read_file(argv[2], xex_bytes) || !read_file(argv[3], image)) {
            std::fprintf(stderr, "error: cannot read XEX or modified basefile\n");
            return 1;
        }
        try {
            xex::XexFile source = xex::XexFile::parse(xex_bytes);
            if (image.size() != source.security().image_size) {
                std::fprintf(stderr, "error: modified basefile size %zu != XEX image size %u\n",
                             image.size(), source.security().image_size);
                return 1;
            }
            std::vector<uint8_t> output = xex::reserialize_with_image(source, image);
            xex::XexFile check = xex::XexFile::parse(output);
            if (!xex::verify_page_hash_chain(check, image)) {
                std::fprintf(stderr, "error: rebuilt XEX page-hash chain did not verify\n");
                return 1;
            }
            std::vector<uint8_t> restored = xex::reconstruct_basefile(check);
            if (restored != image) {
                std::fprintf(stderr, "error: rebuilt XEX did not reconstruct the exact modified basefile\n");
                return 1;
            }
            std::ofstream of(argv[4], std::ios::binary);
            if (!of || !of.write(reinterpret_cast<const char*>(output.data()), std::streamsize(output.size()))) {
                std::fprintf(stderr, "error: cannot write output XEX\n");
                return 1;
            }
            std::printf("rebuilt XEX -> %s (%zu bytes); page-hash chain and basefile round-trip PASS\n",
                        argv[4], output.size());
            return 0;
        } catch (const std::exception& e) {
            std::fprintf(stderr, "replace-basefile: %s\n", e.what());
            return 1;
        }
    }

"""
    m = m.replace(anchor, command + anchor)
    main.write_text(m, encoding="utf-8")

print("Applied T360 XEX page-hash rebuild extension and replace-basefile CLI.")
