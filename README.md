# DiskLens

**An admin disk-space analyzer for Windows that answers one question per drive: what is eating my storage?**

> Work in progress — features land commit by commit.

## Planned features

- See every physical disk and its partitions (EFI, Recovery and hidden ones included)
- Scan a drive and get straight answers: the heaviest **file**, **folder**, **app** and **system item**
- See where the space goes: System · Apps & app data · Your files · Other
- Uninstall heavy apps through **Revo Uninstaller**
- Move files and folders to the **Recycle Bin**, with Windows system files protected

## Build

Requires Windows 10/11 and the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

```bash
dotnet build
```

## License

[MIT](LICENSE)
