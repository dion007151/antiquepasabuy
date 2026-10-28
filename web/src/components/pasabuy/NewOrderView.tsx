import React, { useState } from 'react';
import { useApp } from '../../context/AppContext';
import { useLiveQuery } from 'dexie-react-hooks';
import { db } from '../../db/db';
import {
  Plus,
  Trash2,
  Save,
  UserPlus,
  Truck,
  Building2,
  Info
} from 'lucide-react';
import {
  OrderItem,
  DeliveryType,
  ProductFeeType
} from '../../types';
import {
  calculateOrderSummary,
  roundMoney
} from '../../services/calc';

interface DraftItem {
  id: string;
  productName: string;
  storeName: string;
  qty: number;
  unit: string;
  estimatedPrice: number;
  feeType: ProductFeeType;
  manualFeeAmount?: number;
  notes?: string;
}

export const NewOrderView: React.FC = () => {
  const { setActiveView, addToast } = useApp();

  const customers = useLiveQuery(() => db.customers.where('isActive').equals(1).toArray(), []);
  const stores = useLiveQuery(() => db.stores.toArray(), []);
  const products = useLiveQuery(() => db.products.toArray(), []);
  const rateTiers = useLiveQuery(() => db.rateTiers.toArray(), []);

  // Form State
  const [selectedCustomerId, setSelectedCustomerId] = useState<string>('');
  const [deliveryType, setDeliveryType] = useState<DeliveryType>('DELIVERY');
  const [deliveryAddress, setDeliveryAddress] = useState<string>('');
  const [orderNotes, setOrderNotes] = useState<string>('');
  const [deliveryFee, setDeliveryFee] = useState<number>(50);
  const [discount, setDiscount] = useState<number>(0);

  // Manual Fee Override
  const [useManualFee, setUseManualFee] = useState<boolean>(false);
  const [manualFeeAmount, setManualFeeAmount] = useState<number>(50);
  const [feeOverrideNote, setFeeOverrideNote] = useState<string>('');

  // Items State
  const [items, setItems] = useState<DraftItem[]>([
    {
      id: Math.random().toString(36).substring(2, 9),
      productName: '',
      storeName: 'Grocery Store',
      qty: 1,
      unit: 'pc',
      estimatedPrice: 0,
      feeType: 'STANDARD'
    }
  ]);

  // Quick Customer Modal
  const [showAddCustomer, setShowAddCustomer] = useState<boolean>(false);
  const [newCustName, setNewCustName] = useState<string>('');
  const [newCustPhone, setNewCustPhone] = useState<string>('');
  const [newCustAddress, setNewCustAddress] = useState<string>('');

  // Handle Customer Selection
  const handleCustomerChange = (customerId: string) => {
    setSelectedCustomerId(customerId);
    const cust = customers?.find(c => c.id === customerId);
    if (cust && deliveryType === 'DELIVERY' && !deliveryAddress) {
      setDeliveryAddress(cust.address);
    }
  };

  // Add Item Line
  const handleAddItem = () => {
    setItems(prev => [
      ...prev,
      {
        id: Math.random().toString(36).substring(2, 9),
        productName: '',
        storeName: stores && stores.length > 0 ? stores[0].name : 'Grocery Store',
        qty: 1,
        unit: 'pc',
        estimatedPrice: 0,
        feeType: 'STANDARD'
      }
    ]);
  };

  // Select Item from Product Catalog
  const handleSelectProduct = (index: number, productId: string) => {
    const prod = products?.find(p => p.id === productId);
    if (!prod) return;

    setItems(prev => {
      const updated = [...prev];
      updated[index] = {
        ...updated[index],
        productName: prod.name,
        storeName: prod.store || updated[index].storeName,
        estimatedPrice: prod.price,
        unit: prod.unit || 'pc',
        feeType: prod.feeType || 'STANDARD'
      };
      return updated;
    });
  };

  // Update item field
  const handleItemFieldChange = (index: number, field: keyof DraftItem, value: any) => {
    setItems(prev => {
      const updated = [...prev];
      updated[index] = { ...updated[index], [field]: value };
      return updated;
    });
  };

  // Remove item line
  const handleRemoveItem = (index: number) => {
    if (items.length <= 1) {
      addToast('Order must contain at least one item line', 'error');
      return;
    }
    setItems(prev => prev.filter((_, i) => i !== index));
  };

  // Save quick customer
  const handleSaveQuickCustomer = async () => {
    if (!newCustName.trim()) {
      addToast('Please enter customer name', 'error');
      return;
    }
    const newId = 'c-' + Math.random().toString(36).substring(2, 9);
    const count = (customers?.length || 0) + 1;
    const code = `CUST-${String(count).padStart(3, '0')}`;

    const newCust = {
      id: newId,
      code,
      name: newCustName.trim(),
      phone: newCustPhone.trim(),
      address: newCustAddress.trim(),
      isActive: true,
      createdAt: new Date().toISOString()
    };

    await db.customers.add(newCust);
    setSelectedCustomerId(newId);
    if (newCustAddress) {
      setDeliveryAddress(newCustAddress);
    }
    setShowAddCustomer(false);
    setNewCustName('');
    setNewCustPhone('');
    setNewCustAddress('');
    addToast('Customer added successfully', 'success');
  };

  // Format draft items as OrderItem for calculation engine
  const calculatedItems: OrderItem[] = items.map(item => ({
    id: item.id,
    orderId: 'temp',
    productName: item.productName,
    storeName: item.storeName,
    qty: Number(item.qty) || 1,
    unit: item.unit,
    estimatedPrice: Number(item.estimatedPrice) || 0,
    status: 'PENDING',
    feeType: item.feeType,
    manualFeeAmount: item.manualFeeAmount
  }));

  const summary = calculateOrderSummary(
    {
      items: calculatedItems,
      deliveryFee: deliveryType === 'DELIVERY' ? Number(deliveryFee) || 0 : 0,
      discount: Number(discount) || 0,
      manualFeeOverride: useManualFee ? Number(manualFeeAmount) || 0 : undefined,
      feeOverrideNote: useManualFee ? feeOverrideNote : undefined
    },
    rateTiers
  );

  // Save Final Order
  const handleSaveOrder = async () => {
    if (!selectedCustomerId) {
      addToast('Please select a customer for this order', 'error');
      return;
    }

    const validItems = items.filter(i => i.productName.trim().length > 0);
    if (validItems.length === 0) {
      addToast('Please specify at least one item product name', 'error');
      return;
    }

    if (useManualFee && !feeOverrideNote.trim()) {
      addToast('A note is mandatory when manually overriding the Pasabuy fee', 'error');
      return;
    }

    const cust = customers?.find(c => c.id === selectedCustomerId);
    const orderId = 'ord-' + Math.random().toString(36).substring(2, 9);
    const orderCount = await db.orders.count();
    const orderNo = `ORD-${new Date().getFullYear()}-${String(orderCount + 1).padStart(4, '0')}`;

    const newOrder = {
      id: orderId,
      orderNo,
      customerId: selectedCustomerId,
      customerName: cust?.name || 'Walk-in',
      customerPhone: cust?.phone || '',
      status: 'READY_TO_BUY' as const,
      deliveryType,
      address: deliveryType === 'DELIVERY' ? deliveryAddress : 'Store Pickup',
      notes: orderNotes,
      serviceFee: summary.fee,
      deliveryFee: deliveryType === 'DELIVERY' ? Number(deliveryFee) || 0 : 0,
      discount: Number(discount) || 0,
      manualFeeOverride: useManualFee ? Number(manualFeeAmount) : undefined,
      feeOverrideNote: useManualFee ? feeOverrideNote : undefined,
      isFeeAdjusted: useManualFee,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString()
    };

    const newOrderItems: OrderItem[] = validItems.map(item => ({
      id: 'item-' + Math.random().toString(36).substring(2, 9),
      orderId,
      productName: item.productName.trim(),
      storeName: item.storeName,
      qty: Number(item.qty) || 1,
      unit: item.unit || 'pc',
      estimatedPrice: Number(item.estimatedPrice) || 0,
      status: 'PENDING' as const,
      feeType: item.feeType,
      manualFeeAmount: item.manualFeeAmount,
      notes: item.notes
    }));

    await db.orders.add(newOrder);
    await db.orderItems.bulkAdd(newOrderItems);

    addToast(`Order ${orderNo} created successfully!`, 'success');
    setActiveView('buying-list');
  };

  return (
    <div className="space-y-6">
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
        <div>
          <h2 className="text-lg font-bold text-slate-900">Create Purchase Order</h2>
          <p className="text-xs text-slate-500">
            Set customer details, pasabuy items, and calculate official service fees.
          </p>
        </div>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        {/* Left Column: Customer & Items (2 Cols) */}
        <div className="lg:col-span-2 space-y-6">
          {/* Customer Selection Card */}
          <div className="p-5 bg-white border border-slate-200/80 rounded-2xl shadow-xs space-y-4">
            <div className="flex items-center justify-between">
              <span className="text-xs font-bold text-slate-700 uppercase tracking-wider">
                1. Customer Information
              </span>
              <button
                type="button"
                onClick={() => setShowAddCustomer(true)}
                className="flex items-center gap-1 text-xs font-semibold text-[#452B86] hover:text-[#5B43E8]"
              >
                <UserPlus className="w-3.5 h-3.5" />
                <span>+ Quick New Customer</span>
              </button>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label className="block text-xs font-semibold text-slate-700 mb-1">
                  Select Customer *
                </label>
                <select
                  value={selectedCustomerId}
                  onChange={e => handleCustomerChange(e.target.value)}
                  className="w-full px-3 py-2 text-xs bg-slate-50 border border-slate-200 rounded-xl focus:bg-white focus:outline-none focus:ring-2 focus:ring-purple-600"
                >
                  <option value="">-- Choose Customer --</option>
                  {(customers || []).map(c => (
                    <option key={c.id} value={c.id}>
                      {c.name} ({c.phone || c.code})
                    </option>
                  ))}
                </select>
              </div>

              <div>
                <label className="block text-xs font-semibold text-slate-700 mb-1">
                  Fulfillment Type
                </label>
                <div className="flex bg-slate-100 p-0.5 rounded-xl border border-slate-200 text-xs">
                  <button
                    type="button"
                    onClick={() => setDeliveryType('DELIVERY')}
                    className={`flex-1 flex items-center justify-center gap-1.5 py-1.5 rounded-lg transition-all ${
                      deliveryType === 'DELIVERY'
                        ? 'bg-white font-bold text-purple-800 shadow-xs'
                        : 'text-slate-600'
                    }`}
                  >
                    <Truck className="w-3.5 h-3.5" />
                    <span>Delivery</span>
                  </button>
                  <button
                    type="button"
                    onClick={() => setDeliveryType('PICKUP')}
                    className={`flex-1 flex items-center justify-center gap-1.5 py-1.5 rounded-lg transition-all ${
                      deliveryType === 'PICKUP'
                        ? 'bg-white font-bold text-purple-800 shadow-xs'
                        : 'text-slate-600'
                    }`}
                  >
                    <Building2 className="w-3.5 h-3.5" />
                    <span>Store Pickup</span>
                  </button>
                </div>
              </div>
            </div>

            {deliveryType === 'DELIVERY' && (
              <div>
                <label className="block text-xs font-semibold text-slate-700 mb-1">
                  Delivery Address & Landmark
                </label>
                <input
                  type="text"
                  value={deliveryAddress}
                  onChange={e => setDeliveryAddress(e.target.value)}
                  placeholder="e.g. Brgy. Funda-Dalipe, San Jose (Near Yellow Gate)"
                  className="w-full px-3 py-2 text-xs bg-slate-50 border border-slate-200 rounded-xl focus:bg-white focus:outline-none focus:ring-2 focus:ring-purple-600"
                />
              </div>
            )}
          </div>

          {/* Items Card */}
          <div className="p-5 bg-white border border-slate-200/80 rounded-2xl shadow-xs space-y-4">
            <div className="flex items-center justify-between">
              <div>
                <span className="text-xs font-bold text-slate-700 uppercase tracking-wider">
                  2. Pasabuy Items to Purchase
                </span>
                <p className="text-[11px] text-slate-400">
                  Pick from popular Antique catalog items or type custom items
                </p>
              </div>
              <button
                type="button"
                onClick={handleAddItem}
                className="flex items-center gap-1 px-3 py-1.5 text-xs font-bold text-white bg-[#452B86] hover:bg-[#5B43E8] rounded-xl shadow-xs transition-all active:scale-95"
              >
                <Plus className="w-3.5 h-3.5" />
                <span>Add Item</span>
              </button>
            </div>

            <div className="space-y-3">
              {items.map((item, index) => (
                <div
                  key={item.id}
                  className="p-3.5 bg-slate-50/70 border border-slate-200/90 rounded-2xl space-y-3"
                >
                  <div className="grid grid-cols-12 gap-2.5 items-center">
                    {/* Catalog Helper */}
                    <div className="col-span-12 sm:col-span-5">
                      <label className="block text-[10px] font-bold text-slate-500 uppercase mb-1">
                        Product Name
                      </label>
                      <input
                        type="text"
                        value={item.productName}
                        onChange={e => handleItemFieldChange(index, 'productName', e.target.value)}
                        placeholder="Item name (e.g. 6pc Chickenjoy, Sugar)"
                        className="w-full px-2.5 py-1.5 text-xs bg-white border border-slate-200 rounded-lg focus:outline-none focus:ring-1 focus:ring-purple-600"
                      />
                    </div>

                    {/* Store */}
                    <div className="col-span-6 sm:col-span-3">
                      <label className="block text-[10px] font-bold text-slate-500 uppercase mb-1">
                        Store
                      </label>
                      <select
                        value={item.storeName}
                        onChange={e => handleItemFieldChange(index, 'storeName', e.target.value)}
                        className="w-full px-2 py-1.5 text-xs bg-white border border-slate-200 rounded-lg focus:outline-none"
                      >
                        {(stores || []).map(s => (
                          <option key={s.id} value={s.name}>
                            {s.name}
                          </option>
                        ))}
                      </select>
                    </div>

                    {/* Qty & Unit */}
                    <div className="col-span-3 sm:col-span-2">
                      <label className="block text-[10px] font-bold text-slate-500 uppercase mb-1">
                        Qty
                      </label>
                      <input
                        type="number"
                        min="0.1"
                        step="any"
                        value={item.qty}
                        onChange={e => handleItemFieldChange(index, 'qty', parseFloat(e.target.value) || 0)}
                        className="w-full px-2 py-1.5 text-xs text-center font-bold bg-white border border-slate-200 rounded-lg focus:outline-none"
                      />
                    </div>

                    {/* Estimated Price */}
                    <div className="col-span-3 sm:col-span-2">
                      <label className="block text-[10px] font-bold text-slate-500 uppercase mb-1">
                        Est. Price (₱)
                      </label>
                      <input
                        type="number"
                        min="0"
                        step="any"
                        value={item.estimatedPrice}
                        onChange={e =>
                          handleItemFieldChange(index, 'estimatedPrice', parseFloat(e.target.value) || 0)
                        }
                        className="w-full px-2 py-1.5 text-xs text-right font-bold bg-white border border-slate-200 rounded-lg focus:outline-none"
                      />
                    </div>
                  </div>

                  {/* Secondary Line: Quick Catalog Dropdown + Fee Type + Delete */}
                  <div className="flex flex-wrap items-center justify-between gap-2 pt-2 border-t border-slate-200/60 text-xs">
                    {/* Catalog Pre-fill */}
                    <div className="flex items-center gap-1.5">
                      <span className="text-[10px] text-slate-400">Pick from Catalog:</span>
                      <select
                        onChange={e => {
                          if (e.target.value) handleSelectProduct(index, e.target.value);
                        }}
                        defaultValue=""
                        className="text-[11px] bg-white border border-slate-200 rounded-md px-1.5 py-0.5 text-slate-600 max-w-[160px] truncate"
                      >
                        <option value="">-- Catalog --</option>
                        {(products || []).map(p => (
                          <option key={p.id} value={p.id}>
                            {p.name} (₱{p.price})
                          </option>
                        ))}
                      </select>
                    </div>

                    {/* Fee Type Selector */}
                    <div className="flex items-center gap-2">
                      <div className="flex items-center gap-1">
                        <span className="text-[10px] text-slate-400">Fee:</span>
                        <select
                          value={item.feeType}
                          onChange={e =>
                            handleItemFieldChange(index, 'feeType', e.target.value as ProductFeeType)
                          }
                          className="text-[11px] bg-white border border-slate-200 rounded-md px-1.5 py-0.5 font-medium text-slate-700"
                        >
                          <option value="STANDARD">Standard Tier</option>
                          <option value="NONE">Fee Exempt</option>
                          <option value="MANUAL">Flat Manual Fee</option>
                        </select>
                      </div>

                      {item.feeType === 'MANUAL' && (
                        <input
                          type="number"
                          placeholder="₱ Fee/unit"
                          value={item.manualFeeAmount || ''}
                          onChange={e =>
                            handleItemFieldChange(index, 'manualFeeAmount', parseFloat(e.target.value) || 0)
                          }
                          className="w-16 px-1.5 py-0.5 text-[11px] font-bold bg-white border border-slate-200 rounded-md"
                        />
                      )}

                      <span className="font-bold text-slate-800 text-[11px] ml-1">
                        = ₱{roundMoney(item.qty * item.estimatedPrice).toFixed(2)}
                      </span>

                      <button
                        type="button"
                        onClick={() => handleRemoveItem(index)}
                        className="p-1 text-slate-400 hover:text-rose-600 hover:bg-rose-50 rounded-md transition-colors"
                        title="Remove Item"
                      >
                        <Trash2 className="w-3.5 h-3.5" />
                      </button>
                    </div>
                  </div>
                </div>
              ))}
            </div>

            <button
              type="button"
              onClick={handleAddItem}
              className="w-full py-2 text-xs font-semibold text-purple-700 bg-purple-50/70 border border-dashed border-purple-200 rounded-xl hover:bg-purple-100 transition-colors"
            >
              + Add Another Item
            </button>
          </div>
        </div>

        {/* Right Column: Pricing & Calculation Summary */}
        <div className="space-y-6">
          <div className="p-5 bg-white border border-slate-200/80 rounded-2xl shadow-xs space-y-4 sticky top-20">
            <div className="flex items-center justify-between pb-2 border-b border-slate-100">
              <span className="text-xs font-bold text-slate-700 uppercase tracking-wider">
                Order Financial Summary
              </span>
              <span className="text-[10px] font-semibold text-purple-700 bg-purple-50 px-2 py-0.5 rounded-full">
                Auto-Calculated
              </span>
            </div>

            <div className="space-y-2.5 text-xs">
              <div className="flex justify-between text-slate-600">
                <span>Items Subtotal:</span>
                <span className="font-bold text-slate-900">₱{summary.subtotal.toFixed(2)}</span>
              </div>

              {/* Service Fee */}
              <div className="flex justify-between items-center text-slate-600">
                <div className="flex items-center gap-1">
                  <span>Pasabuy Fee:</span>
                  <div className="group relative">
                    <Info className="w-3.5 h-3.5 text-slate-400 cursor-help" />
                    <div className="absolute right-0 bottom-full mb-1 hidden group-hover:block w-48 p-2 bg-slate-800 text-[10px] text-white rounded-lg shadow-lg z-20">
                      Computed from official Antique Pasabuy rate table. Subtotal ₱2500+ is 15%.
                    </div>
                  </div>
                </div>
                <span className="font-bold text-purple-700">₱{summary.fee.toFixed(2)}</span>
              </div>

              {/* Manual Override Option */}
              <div className="pt-1">
                <label className="flex items-center gap-1.5 text-[11px] text-slate-600 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={useManualFee}
                    onChange={e => setUseManualFee(e.target.checked)}
                    className="rounded text-purple-600"
                  />
                  <span>Manual Fee Override</span>
                </label>

                {useManualFee && (
                  <div className="mt-2 space-y-2 p-2.5 bg-amber-50/70 border border-amber-200 rounded-xl">
                    <div>
                      <span className="block text-[10px] font-bold text-amber-800 uppercase mb-0.5">
                        Override Fee (₱)
                      </span>
                      <input
                        type="number"
                        min="0"
                        value={manualFeeAmount}
                        onChange={e => setManualFeeAmount(parseFloat(e.target.value) || 0)}
                        className="w-full px-2 py-1 text-xs font-bold bg-white border border-amber-300 rounded-lg"
                      />
                    </div>
                    <div>
                      <span className="block text-[10px] font-bold text-amber-800 uppercase mb-0.5">
                        Mandatory Reason/Note *
                      </span>
                      <input
                        type="text"
                        placeholder="e.g. Special heavy cargo or promo"
                        value={feeOverrideNote}
                        onChange={e => setFeeOverrideNote(e.target.value)}
                        className="w-full px-2 py-1 text-xs bg-white border border-amber-300 rounded-lg"
                      />
                    </div>
                  </div>
                )}
              </div>

              {/* Delivery Fee Input */}
              {deliveryType === 'DELIVERY' && (
                <div className="flex justify-between items-center text-slate-600">
                  <span>Rider Delivery Fee:</span>
                  <div className="flex items-center gap-1">
                    <span className="text-slate-400">₱</span>
                    <input
                      type="number"
                      min="0"
                      value={deliveryFee}
                      onChange={e => setDeliveryFee(parseFloat(e.target.value) || 0)}
                      className="w-20 px-2 py-1 text-right text-xs font-bold bg-slate-50 border border-slate-200 rounded-lg"
                    />
                  </div>
                </div>
              )}

              {/* Discount */}
              <div className="flex justify-between items-center text-slate-600">
                <span>Discount / Promo:</span>
                <div className="flex items-center gap-1">
                  <span className="text-slate-400">-₱</span>
                  <input
                    type="number"
                    min="0"
                    value={discount}
                    onChange={e => setDiscount(parseFloat(e.target.value) || 0)}
                    className="w-20 px-2 py-1 text-right text-xs font-bold bg-slate-50 border border-slate-200 rounded-lg text-emerald-600"
                  />
                </div>
              </div>

              {/* Grand Total */}
              <div className="pt-3 border-t-2 border-slate-200 flex justify-between items-baseline">
                <span className="text-sm font-bold text-slate-900">Total Bill:</span>
                <span className="text-xl font-black text-[#452B86]">
                  ₱{summary.grandTotal.toFixed(2)}
                </span>
              </div>
            </div>

            {/* Notes */}
            <div>
              <label className="block text-[11px] font-semibold text-slate-700 mb-1">
                Order Notes / Dedication
              </label>
              <textarea
                rows={2}
                value={orderNotes}
                onChange={e => setOrderNotes(e.target.value)}
                placeholder="Special notes, preferred delivery time, cake dedication..."
                className="w-full px-2.5 py-1.5 text-xs bg-slate-50 border border-slate-200 rounded-xl focus:bg-white focus:outline-none"
              />
            </div>

            {/* Submit Button */}
            <button
              type="button"
              onClick={handleSaveOrder}
              className="w-full flex items-center justify-center gap-2 py-3 text-xs font-bold text-white bg-gradient-to-r from-[#452B86] to-[#5B43E8] hover:from-[#5B43E8] hover:to-[#452B86] rounded-xl shadow-md transition-all active:scale-95"
            >
              <Save className="w-4 h-4" />
              <span>Save & Place Order</span>
            </button>
          </div>
        </div>
      </div>

      {/* Quick Add Customer Modal */}
      {showAddCustomer && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/50 backdrop-blur-xs">
          <div className="w-full max-w-md bg-white rounded-2xl shadow-xl p-5 border border-slate-200 space-y-4">
            <h3 className="font-bold text-sm text-slate-900">Add New Customer</h3>
            <div className="space-y-3">
              <div>
                <label className="block text-xs font-semibold text-slate-700 mb-1">Full Name *</label>
                <input
                  type="text"
                  value={newCustName}
                  onChange={e => setNewCustName(e.target.value)}
                  placeholder="e.g. Maria Santos"
                  className="w-full px-3 py-2 text-xs bg-slate-50 border border-slate-200 rounded-xl focus:bg-white focus:outline-none"
                />
              </div>
              <div>
                <label className="block text-xs font-semibold text-slate-700 mb-1">Mobile Phone</label>
                <input
                  type="text"
                  value={newCustPhone}
                  onChange={e => setNewCustPhone(e.target.value)}
                  placeholder="e.g. 0917-123-4567"
                  className="w-full px-3 py-2 text-xs bg-slate-50 border border-slate-200 rounded-xl focus:bg-white focus:outline-none"
                />
              </div>
              <div>
                <label className="block text-xs font-semibold text-slate-700 mb-1">Delivery Address</label>
                <input
                  type="text"
                  value={newCustAddress}
                  onChange={e => setNewCustAddress(e.target.value)}
                  placeholder="e.g. Brgy. Funda-Dalipe, San Jose"
                  className="w-full px-3 py-2 text-xs bg-slate-50 border border-slate-200 rounded-xl focus:bg-white focus:outline-none"
                />
              </div>
            </div>
            <div className="flex gap-2 pt-2">
              <button
                type="button"
                onClick={() => setShowAddCustomer(false)}
                className="flex-1 py-2 text-xs font-semibold text-slate-700 bg-slate-100 rounded-xl hover:bg-slate-200"
              >
                Cancel
              </button>
              <button
                type="button"
                onClick={handleSaveQuickCustomer}
                className="flex-1 py-2 text-xs font-bold text-white bg-[#452B86] rounded-xl hover:bg-[#5B43E8]"
              >
                Save Customer
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
