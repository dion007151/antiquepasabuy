# 📦 Antique Pasabuy POS — Standalone Web App with Inventory Management

This web app is a **dedicated, standalone Antique Pasabuy POS system** designed to run **100% free forever on Vercel** ($0.00/month, zero server costs, zero database bills, and offline PWA capability).

---

## 🌟 What's Included in Your Pasabuy POS

### 1. 📦 Complete On-Hand Inventory Management (`InventoryView.tsx`)
- **Stock Reconciliation Table**:
  - **Opening Stock**: Initial batch inventory count.
  - **Received (+)**: Additional stocks restocked during the period.
  - **Total Stocks**: `Opening + Additional`.
  - **Sold / Dispatched**: Automatically calculated as items are fulfilled in orders.
  - **Current On-Hand**: Live remaining balance.
  - **Stock Status**: Normal (green), Low Stock (amber), or Out of Stock (red).
- **Stock KPI Cards**:
  - Total Tracked Products
  - Total Units On-Hand
  - Low Stock Alerts (items at or below minimum threshold)
  - Estimated Inventory Asset Value (at cost price)
- **Restock & Adjust Tools**:
  - **+ Restock Inventory**: Add incoming stock units with a supplier / source note.
  - **Adjust Stock Count**: Correct physical stock with a mandatory reason note (*damaged packaging, physical inventory recount, spoilage*).
- **Movements Audit Log**:
  - Chronological audit trail showing every stock addition (+), order deduction (-), and manual adjustment with timestamps and notes.

### 2. 🛒 Supermarket Buying List with Direct "From Stock" Fulfillment
- When shoppers are buying items, each item displays whether it is currently available in your on-hand inventory.
- Shoppers can either click **"Bought"** (at supermarket) OR click **"From Stock"**, which automatically fulfills the order item from your warehouse/store stock, decrements inventory, and records an audit log entry!

### 3. 🏷️ Product Catalog with Inventory Flags
- Mark any catalog item as **"Track In-Stock Inventory (STOCK Item)"**.
- Set opening stock quantity, wholesale cost price, selling price, store/aisle, and minimum alert threshold.

### 4. ⚡ 100% Free Vercel Deployment & Offline Storage
- Runs entirely on **Local-First IndexedDB (`Dexie.js`)**.
- Works with zero internet in supermarkets or remote barangays.
- **1-Click Backup & Restore**: Download a `.json` backup file anytime or restore it on any phone, tablet, or PC.

---

## 🚀 How to Deploy to Vercel for Free

### Option 1: 1-Click via GitHub
1. Push your repository to GitHub:
   ```bash
   git add .
   git commit -m "Standalone Antique Pasabuy POS with Inventory"
   git push origin main
   ```
2. Log in to [vercel.com](https://vercel.com/) (Hobby free plan).
3. Click **"Add New Project"** and select your repository.
4. Leave settings as default (root `vercel.json` and `package.json` are already pre-configured to build `web/dist`).
5. Click **"Deploy"**!

### Option 2: Run Locally on Your PC or Tablet
```powershell
cd web
npm run dev
```
Open `http://localhost:5173/` in your browser.
