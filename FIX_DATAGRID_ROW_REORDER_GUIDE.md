# 🔧 Hướng Dẫn Sửa Lỗi DataGrid Thay Đổi Thứ Tự Dòng

## ❌ **Vấn Đề**
Khi nhập ký tự vào cột **Notes** trong DataGrid, các dòng bị **thay đổi thứ tự** hoặc **re-sort** một cách không mong muốn.

---

## 🎯 **Nguyên Nhân Chính**

### **1. Người dùng có thể click vào header cột để sort**
- DataGrid mặc định cho phép sort bằng cách click vào header
- Khi có `CanUserSortColumns="True"` (hoặc không khai báo), click header sẽ sắp xếp dữ liệu
- Điều này gây nhầm lẫn khi đang editing

### **2. Property Note không có PropertyChanged notification**
- Các property như `IdCam`, `EnableCam`, `Enable` là **auto-property** (chỉ `{ get; set; }`)
- Khi binding hai chiều (`Mode=TwoWay`), nếu property không thông báo thay đổi, DataGrid có thể refresh
- Một số phiên bản WPF có hành vi re-sort khi collection thay đổi

### **3. Virtualization + Recycling có thể gây hiệu ứng lạ**
- `VirtualizingPanel.VirtualizationMode="Recycling"` có thể tái sử dụng container
- Nếu sort không được vô hiệu hóa, dữ liệu có thể bị shuffle

---

## ✅ **Giải Pháp Áp Dụng**

### **1️⃣ Thêm PropertyChanged cho tất cả Properties trong ForwardPoint**

**File:** `WpfCompanyApp\Models\ForwardPoint.cs`

```csharp
public class ForwardPoint : INotifyPropertyChanged
{
    public int Index { get; set; }

    private double velocity;
    public double Velocity
    {
        get => velocity;
        set { velocity = value; OnPropertyChanged(nameof(Velocity)); }
    }

    private RobotTrajectory.MoveTypeEnum moveType;
    public RobotTrajectory.MoveTypeEnum MoveType
    {
        get => moveType;
        set { moveType = value; OnPropertyChanged(nameof(MoveType)); }
    }

    private int idCam;
    public int IdCam
    {
        get => idCam;
        set { idCam = value; OnPropertyChanged(nameof(IdCam)); }
    }

    private bool enableCam;
    public bool EnableCam
    {
        get => enableCam;
        set { enableCam = value; OnPropertyChanged(nameof(EnableCam)); }
    }

    private bool enable;
    public bool Enable
    {
        get => enable;
        set { enable = value; OnPropertyChanged(nameof(Enable)); }
    }

    private string note = "";
    public string Note
    {
        get => note;
        set { note = value; OnPropertyChanged(nameof(Note)); }
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
```

**🔑 Lợi ích:**
- ✅ Mọi thay đổi đều được thông báo đúng cách
- ✅ Binding TwoWay hoạt động trơn tru
- ✅ DataGrid không bị re-render không cần thiết

---

### **2️⃣ Vô Hiệu Hóa Sort trong XAML**

**File:** `WpfCompanyApp\Views\SettingsView.xaml`

```xaml
<DataGrid Grid.Row="1"
          ...
          CanUserSortColumns="False"
          ...>
```

**🔑 Tác dụng:**
- ✅ Người dùng **không thể click header để sort**
- ✅ DataGrid luôn giữ thứ tự theo **Index** (từ 1 đến 100)
- ✅ Không bị re-order khi nhập liệu

---

### **3️⃣ Cải Thiện ForwardPoints Property**

**File:** `WpfCompanyApp\ViewModels\SettingsViewModel.cs`

**Cũ:**
```csharp
public ObservableCollection<ForwardPoint> ForwardPoints { get; set; }
```

**Mới:**
```csharp
private ObservableCollection<ForwardPoint> forwardPoints;
public ObservableCollection<ForwardPoint> ForwardPoints
{
    get => forwardPoints;
    set => SetProperty(ref forwardPoints, value);
}
```

**🔑 Lợi ích:**
- ✅ Sử dụng `SetProperty` để thông báo khi collection thay đổi
- ✅ Kế thừa từ `ViewModelBase` (MVVM Toolkit)
- ✅ Property theo chuẩn MVVM

---

## 🧪 **Kiểm Tra Kết Quả**

Sau khi áp dụng fix, hãy:

1. **Mở Settings Tab** → "SETTING ROBOT TRAJECTORY"
2. **Click vào cột Notes ở bất kỳ dòng nào** (ví dụ dòng 10)
3. **Gõ ký tự** (ví dụ: "Test", "ABC", v.v.)
4. **Quan sát:**
   - ✅ Dòng **giữ nguyên vị trí** (không bị move)
   - ✅ Chữ được nhập **bình thường**
   - ✅ Dòng 10 **vẫn là dòng 10**

---

## 📋 **Tóm Tắt Các Thay Đổi**

| File | Thay Đổi | Lý Do |
|------|---------|-------|
| `ForwardPoint.cs` | Thêm PropertyChanged cho tất cả properties | Đảm bảo binding TwoWay hoạt động đúng |
| `SettingsView.xaml` | Thêm `CanUserSortColumns="False"` | Vô hiệu hóa sort, giữ thứ tự |
| `SettingsViewModel.cs` | Dùng `SetProperty` cho ForwardPoints | Tuân theo chuẩn MVVM |

---

## 🚀 **Best Practices**

### ✅ **Luôn sử dụng PropertyChanged trong Models:**
```csharp
private string myProperty;
public string MyProperty
{
    get => myProperty;
    set { myProperty = value; OnPropertyChanged(nameof(MyProperty)); }
}
```

### ✅ **Vô hiệu hóa sorting nếu không cần:**
```xaml
CanUserSortColumns="False"
```

### ✅ **Sử dụng ObservableCollection<T> đúng cách:**
- Chỉ `.Add()`, `.Remove()`, `.Clear()` từ UI thread
- Không nên `= new List<>()` khi DataGrid đang bind

### ❌ **Tránh:**
- Auto-properties cho mutable data
- Cho phép sort khi không cần thiết
- Rebuild collection khi chỉ cần update item

---

## 📚 **Tham Khảo Thêm**

- [INotifyPropertyChanged Documentation](https://docs.microsoft.com/en-us/dotnet/api/system.componentmodel.inotifypropertychanged)
- [WPF DataGrid Column Sorting](https://docs.microsoft.com/en-us/dotnet/desktop/wpf/controls/datagrid)
- [MVVM Community Toolkit](https://github.com/CommunityToolkit/dotnet)
