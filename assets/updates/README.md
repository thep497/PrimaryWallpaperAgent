# Update verification key

update-public-key.pem is the public ECDSA P-256 verification key embedded in both distribution flavors.

The private key is excluded from source and release assets. Publish uses the DPAPI-protected developer key under .local/release-signing. Do not regenerate a key just because the private file is unavailable; existing clients trust this public key.

Changing this public key requires an explicit trust migration design. It is not a branding asset or an Authenticode certificate.
