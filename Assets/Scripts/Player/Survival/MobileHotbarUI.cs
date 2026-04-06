using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MobileHotbarUI : MonoBehaviour
{
    [Header("Reference")]
    public PlayerInventory inventory;
    public NetworkInventoryBridge networkBridge;

    [Header("UI")]
    public List<Button> slotButtons = new List<Button>();
    public List<Image> slotIcons = new List<Image>();

    [Header("Setting")]
    public int maxSlots = 5;

    [Header("Visual")]
    public Color normalColor = Color.white;
    public Color selectedColor = Color.yellow;

    [Header("Mobile Input")]
    public float swipeThreshold = 50f;

    [Header("Icon")]
    public ItemIconDatabase iconDatabase;

    [Header("Item Count UI")]
    public List<TMP_Text> slotCounts = new List<TMP_Text>();

    private ItemType?[] slotItems;
    private int selectedSlot = -1;

    void Start()
    {
        slotItems = new ItemType?[maxSlots];
        if (inventory != null)
        {
            inventory.InventoryChanged += OnInventoryChanged;
        }

        inventory.InventoryChanged += Refresh;
        Refresh();
    }

    void Update()
    {
        HandleSwipe();
    }

    void OnDisable()
    {
        if (inventory != null)
        {
            inventory.InventoryChanged -= OnInventoryChanged;
        }
    }

    void OnInventoryChanged()
    {
        AutoAssignFromInventory();
        Refresh();
    }

    void AutoAssignFromInventory()
    {
        foreach (var entry in inventory.Entries)
        {
            ItemType type = entry.itemType;

            // cek apakah sudah ada di hotbar
            bool alreadyExists = false;
            for (int i = 0; i < maxSlots; i++)
            {
                if (slotItems[i] != null && slotItems[i] == type)
                {
                    alreadyExists = true;
                    break;
                }
            }

            if (alreadyExists) continue;

            // cari slot kosong
            for (int i = 0; i < maxSlots; i++)
            {
                if (slotItems[i] == null)
                {
                    slotItems[i] = type;
                    break;
                }
            }
        }
    }

    void Refresh()
    {
        for (int i = 0; i < maxSlots; i++)
        {
            // 🔥 DEFAULT VISUAL SLOT
            slotButtons[i].image.color = (i == selectedSlot) ? selectedColor : normalColor;

            if (slotItems[i] == null)
            {
                slotIcons[i].enabled = false;
                slotCounts[i].text = "";
                slotCounts[i].gameObject.SetActive(false);
                continue;
            }

            ItemType type = slotItems[i].Value;
            int amount = inventory.GetAmount(type);

            if (amount <= 0)
            {
                slotItems[i] = null;
                slotIcons[i].enabled = false;
                slotCounts[i].text = "";
                slotCounts[i].gameObject.SetActive(false);
                continue;
            }

            // ICON
            Sprite icon = iconDatabase.GetIcon(type);
            slotIcons[i].sprite = icon;
            slotIcons[i].enabled = true;

            // 🔢 COUNT
            slotCounts[i].text = amount.ToString();
            slotCounts[i].gameObject.SetActive(true);
        }
    }

    public void SelectSlot(int slotIndex)
    {
        selectedSlot = slotIndex;
        Refresh();
    }

    void UseItem(ItemType itemType)
    {
        Debug.Log("Use item: " + itemType);

        // 🔥 MULTIPLAYER SAFE
        if (networkBridge != null && networkBridge.UseNetworkedInventory)
        {
            bool success = networkBridge.TryRequestDrop(itemType, 1);

            if (!success)
            {
                Debug.Log("Gagal pakai item (no authority)");
            }

            return;
        }

        // LOCAL
        inventory.DropItem(itemType, 1);
    }

    void HandleSwipe()
    {
        if (Input.touchCount == 0) return;

        Touch touch = Input.GetTouch(0);

        if (touch.phase == TouchPhase.Ended)
        {
            if (touch.deltaPosition.x > swipeThreshold)
            {
                SelectNext();
            }
            else if (touch.deltaPosition.x < -swipeThreshold)
            {
                SelectPrevious();
            }
        }
    }

    void SelectNext()
    {
        selectedSlot = (selectedSlot + 1) % maxSlots;
        SelectSlot(selectedSlot);
    }

    void SelectPrevious()
    {
        selectedSlot--;

        if (selectedSlot < 0)
            selectedSlot = maxSlots - 1;

        SelectSlot(selectedSlot);
    }

    public void DropFromSlot(int index)
    {
        if (slotItems[index] == null) return;

        ItemType type = slotItems[index].Value;

        if (networkBridge != null && networkBridge.UseNetworkedInventory)
        {
            networkBridge.TryRequestDrop(type, 1);
        }
        else
        {
            inventory.DropItem(type, 1);
        }
    }

    public void SwapSlot(int a, int b)
    {
        var temp = slotItems[a];
        slotItems[a] = slotItems[b];
        slotItems[b] = temp;

        Refresh();
    }

    public void AssignToSlot(int slotIndex, ItemType type)
    {
        slotItems[slotIndex] = type;
        Refresh();
    }
}