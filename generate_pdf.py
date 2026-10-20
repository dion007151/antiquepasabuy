import os
from reportlab.lib import colors
from reportlab.lib.pagesizes import A4
from reportlab.platypus import (
    SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle, HRFlowable, KeepTogether
)
from reportlab.lib.styles import getSampleStyleSheet, ParagraphStyle

def generate_pdf(output_path):
    doc = SimpleDocTemplate(
        output_path,
        pagesize=A4,
        leftMargin=36,
        rightMargin=36,
        topMargin=32,
        bottomMargin=32
    )

    styles = getSampleStyleSheet()

    # Custom typography
    brand_style = ParagraphStyle(
        'BrandTitle',
        parent=styles['Normal'],
        fontName='Helvetica-Bold',
        fontSize=20,
        leading=22,
        textColor=colors.HexColor('#1e1b4b')
    )
    brand_sub = ParagraphStyle(
        'BrandSub',
        parent=styles['Normal'],
        fontName='Helvetica',
        fontSize=9,
        leading=12,
        textColor=colors.HexColor('#64748b')
    )
    badge_style = ParagraphStyle(
        'Badge',
        parent=styles['Normal'],
        fontName='Helvetica-Bold',
        fontSize=8,
        leading=10,
        textColor=colors.HexColor('#4338ca')
    )
    meta_style = ParagraphStyle(
        'Meta',
        parent=styles['Normal'],
        fontName='Helvetica',
        fontSize=8.5,
        leading=11,
        alignment=2,  # Right aligned
        textColor=colors.HexColor('#334155')
    )
    sec_title = ParagraphStyle(
        'SecTitle',
        parent=styles['Normal'],
        fontName='Helvetica-Bold',
        fontSize=10,
        leading=13,
        textColor=colors.HexColor('#1e1b4b'),
        spaceAfter=4
    )
    table_hdr = ParagraphStyle(
        'TableHdr',
        parent=styles['Normal'],
        fontName='Helvetica-Bold',
        fontSize=8,
        leading=10,
        textColor=colors.white
    )
    table_cell = ParagraphStyle(
        'TableCell',
        parent=styles['Normal'],
        fontName='Helvetica',
        fontSize=8,
        leading=10.5,
        textColor=colors.HexColor('#1e293b')
    )
    table_cell_bold = ParagraphStyle(
        'TableCellBold',
        parent=styles['Normal'],
        fontName='Helvetica-Bold',
        fontSize=8,
        leading=10.5,
        textColor=colors.HexColor('#0f172a')
    )
    table_cell_right = ParagraphStyle(
        'TableCellRight',
        parent=styles['Normal'],
        fontName='Helvetica-Bold',
        fontSize=8,
        leading=10.5,
        alignment=2,
        textColor=colors.HexColor('#334155')
    )

    story = []

    # Header section
    hdr_data = [
        [
            Paragraph("<b>OFFICIAL SYSTEM VALUATION & PROPOSAL</b>", badge_style),
            Paragraph("<b>Ref:</b> DPOS-2026-VAL | <b>Date:</b> October 12, 2026", meta_style)
        ],
        [
            Paragraph("DIONIX POS / Antique Pasabuy System", brand_style),
            Paragraph("<b>Platform:</b> Windows Desktop (.NET 8 / WPF)<br/><b>Database:</b> High-Performance SQLite WAL", meta_style)
        ],
        [
            Paragraph("Enterprise Point of Sale & Multi-Customer Fulfillment Architecture", brand_sub),
            Paragraph("<b>Validity:</b> 30 Days", meta_style)
        ]
    ]
    hdr_table = Table(hdr_data, colWidths=[340, 183])
    hdr_table.setStyle(TableStyle([
        ('VALIGN', (0, 0), (-1, -1), 'TOP'),
        ('BOTTOMPADDING', (0, 0), (-1, -1), 2),
        ('TOPPADDING', (0, 0), (-1, -1), 0),
        ('LEFTPADDING', (0, 0), (-1, -1), 0),
        ('RIGHTPADDING', (0, 0), (-1, -1), 0),
    ]))
    story.append(hdr_table)
    story.append(Spacer(1, 6))
    story.append(HRFlowable(width="100%", thickness=2.5, color=colors.HexColor('#6366f1'), spaceAfter=10))

    # Parties Card
    party_data = [
        [
            Paragraph("<b>CLIENT DETAILS (CLIENT)</b>", ParagraphStyle('P1', fontName='Helvetica-Bold', fontSize=7.5, textColor=colors.HexColor('#64748b'))),
            Paragraph("<b>DEVELOPER / VENDOR</b>", ParagraphStyle('P2', fontName='Helvetica-Bold', fontSize=7.5, textColor=colors.HexColor('#64748b')))
        ],
        [
            Paragraph("<b>Valued Retail / Pasabuy Client</b><br/>Antique Province, Philippines<br/><font color='#64748b' size=7.5>Package: Full Dual-Mode License</font>", ParagraphStyle('P3', fontName='Helvetica', fontSize=8.5, leading=11)),
            Paragraph("<b>Dioni / DIONIX Software Solutions</b><br/>Custom POS & Business Software Engineering<br/><font color='#64748b' size=7.5>Specialized Desktop Software Development</font>", ParagraphStyle('P4', fontName='Helvetica', fontSize=8.5, leading=11))
        ]
    ]
    party_table = Table(party_data, colWidths=[260, 263])
    party_table.setStyle(TableStyle([
        ('BACKGROUND', (0, 0), (-1, -1), colors.HexColor('#f8fafc')),
        ('BOX', (0, 0), (-1, -1), 0.8, colors.HexColor('#e2e8f0')),
        ('INNERGRID', (0, 0), (-1, -1), 0.5, colors.HexColor('#e2e8f0')),
        ('TOPPADDING', (0, 0), (-1, -1), 6),
        ('BOTTOMPADDING', (0, 0), (-1, -1), 6),
        ('LEFTPADDING', (0, 0), (-1, -1), 8),
        ('RIGHTPADDING', (0, 0), (-1, -1), 8),
    ]))
    story.append(party_table)
    story.append(Spacer(1, 10))

    # Modules Breakdown
    story.append(Paragraph("SYSTEM MODULE VALUATION BREAKDOWN (STANDALONE MARKET VALUE)", sec_title))

    modules = [
        ("1", "Dual-Mode POS Architecture", "Pasabuy Order Fulfillment & Full Restaurant / Dine-in POS in one unified desktop suite", "PHP 22,000.00"),
        ("2", "Shopping & Logistics Engine", "Store-grouped buying list, real-time camera/barcode scanning, out-of-stock replanning", "PHP 16,000.00"),
        ("3", "Inventory & Product Catalog", "Multi-store catalog, product images, barcode assignment, stock ledger & quantity alerts", "PHP 14,000.00"),
        ("4", "Customer & Store Management", "Customer credit/balance tracking, purchase history, store directory, delivery destinations", "PHP 9,000.00"),
        ("5", "Multi-Channel Payments", "Cash, GCash, Maya, Bank Transfer, COD; partial downpayments, auto balance due computation", "PHP 12,000.00"),
        ("6", "Restaurant & Table Ops", "Floor plan & table layout, kitchen tickets, order add-ons, dine-in/take-out billing", "PHP 18,000.00"),
        ("7", "Reports, Expenses & Printing", "Daily/monthly sales KPIs, net income calculator, expense journal, 58/80mm thermal receipt printing", "PHP 11,000.00"),
        ("8", "Enterprise Database & Security", "Role-based access (Admin, Staff, Shopper), SQLite WAL concurrent engine, zero cloud fees", "PHP 8,000.00"),
    ]

    mod_rows = [[
        Paragraph("<b>#</b>", table_hdr),
        Paragraph("<b>MODULE / COMPONENT</b>", table_hdr),
        Paragraph("<b>SCOPE & INCLUDED FUNCTIONALITY</b>", table_hdr),
        Paragraph("<b>MARKET VALUE</b>", ParagraphStyle('THRight', parent=table_hdr, alignment=2))
    ]]

    for num, title, desc, val in modules:
        mod_rows.append([
            Paragraph(num, ParagraphStyle('C', parent=table_cell, alignment=1)),
            Paragraph(f"<b>{title}</b>", table_cell_bold),
            Paragraph(desc, table_cell),
            Paragraph(val, table_cell_right)
        ])

    mod_table = Table(mod_rows, colWidths=[20, 140, 263, 100])
    mod_style = [
        ('BACKGROUND', (0, 0), (-1, 0), colors.HexColor('#1e1b4b')),
        ('VALIGN', (0, 0), (-1, -1), 'MIDDLE'),
        ('TOPPADDING', (0, 0), (-1, -1), 4.5),
        ('BOTTOMPADDING', (0, 0), (-1, -1), 4.5),
        ('LEFTPADDING', (0, 0), (-1, -1), 5),
        ('RIGHTPADDING', (0, 0), (-1, -1), 5),
        ('GRID', (0, 0), (-1, -1), 0.5, colors.HexColor('#e2e8f0')),
    ]
    for i in range(1, len(mod_rows)):
        if i % 2 == 0:
            mod_style.append(('BACKGROUND', (0, i), (-1, i), colors.HexColor('#f8fafc')))
    mod_table.setStyle(TableStyle(mod_style))
    story.append(mod_table)
    story.append(Spacer(1, 10))

    # Pricing Summary Callout
    callout_data = [
        [
            Paragraph(
                "<b>SPECIAL CLIENT BUDGET CONCESSION</b><br/>"
                "<font size=7.5 color='#166534'>"
                "To support the client's business launch and accommodate financial constraints, "
                "the developer has granted a <b>special developer sponsorship discount of 72.7% (PHP 80,000.00 off)</b>. "
                "The client receives the exact same high-performance, enterprise-grade system without any feature limitations."
                "</font>",
                ParagraphStyle('Callout', fontName='Helvetica', fontSize=8, leading=11, textColor=colors.HexColor('#14532d'))
            ),
            Paragraph(
                "<table width='100%'>"
                "<tr><td><font size=7.5 color='#64748b'><b>Market Valuation:</b></font></td><td align='right'><font size=7.5 color='#94a3b8'><strike>PHP 110,000.00</strike></font></td></tr>"
                "<tr><td><font size=7.5 color='#16a34a'><b>Budget Concession:</b></font></td><td align='right'><font size=7.5 color='#16a34a'><b>- PHP 80,000.00</b></font></td></tr>"
                "<tr><td colspan='2'><hr color='#6366f1'/></td></tr>"
                "<tr><td><font size=9.5 color='#312e81'><b>FINAL OFFER:</b></font></td><td align='right'><font size=11 color='#4338ca'><b>PHP 30,000.00</b></font></td></tr>"
                "</table>",
                ParagraphStyle('Tot', fontName='Helvetica', fontSize=8, leading=10.5)
            )
        ]
    ]
    callout_table = Table(callout_data, colWidths=[310, 213])
    callout_table.setStyle(TableStyle([
        ('BACKGROUND', (0, 0), (0, 0), colors.HexColor('#f0fdf4')),
        ('BACKGROUND', (1, 0), (1, 0), colors.HexColor('#f8fafc')),
        ('BOX', (0, 0), (0, 0), 0.8, colors.HexColor('#86efac')),
        ('BOX', (1, 0), (1, 0), 1.2, colors.HexColor('#6366f1')),
        ('TOPPADDING', (0, 0), (-1, -1), 8),
        ('BOTTOMPADDING', (0, 0), (-1, -1), 8),
        ('LEFTPADDING', (0, 0), (-1, -1), 8),
        ('RIGHTPADDING', (0, 0), (-1, -1), 8),
        ('VALIGN', (0, 0), (-1, -1), 'MIDDLE'),
    ]))
    story.append(callout_table)
    story.append(Spacer(1, 10))

    # Package Inclusions
    story.append(Paragraph("WHAT IS INCLUDED IN THE PHP 30,000.00 PACKAGE", sec_title))

    inc_data = [
        [
            Paragraph("<b>1. Lifetime Software License</b><br/><font size=7.2 color='#475569'>Full unrestricted access to Pasabuy & Restaurant modules. Never pay monthly subscriptions or cloud fees.</font>", ParagraphStyle('I1', fontName='Helvetica', fontSize=8, leading=10)),
            Paragraph("<b>2. Complete Installation & Deployment</b><br/><font size=7.2 color='#475569'>Installation on client's PC/laptop, database configuration, receipt printer & barcode scanner calibration.</font>", ParagraphStyle('I2', fontName='Helvetica', fontSize=8, leading=10))
        ],
        [
            Paragraph("<b>3. Hands-On Staff & Admin Training</b><br/><font size=7.2 color='#475569'>Step-by-step training on taking orders, buying checklist navigation, payments, end-of-day sales auditing.</font>", ParagraphStyle('I3', fontName='Helvetica', fontSize=8, leading=10)),
            Paragraph("<b>4. 3 Months Free Technical Support</b><br/><font size=7.2 color='#475569'>Direct developer support for bug fixes, database backup assistance, and operational questions.</font>", ParagraphStyle('I4', fontName='Helvetica', fontSize=8, leading=10))
        ]
    ]
    inc_table = Table(inc_data, colWidths=[260, 263])
    inc_table.setStyle(TableStyle([
        ('BACKGROUND', (0, 0), (-1, -1), colors.HexColor('#faf5ff')),
        ('BOX', (0, 0), (-1, -1), 0.6, colors.HexColor('#e9d5ff')),
        ('INNERGRID', (0, 0), (-1, -1), 0.5, colors.HexColor('#e9d5ff')),
        ('TOPPADDING', (0, 0), (-1, -1), 6),
        ('BOTTOMPADDING', (0, 0), (-1, -1), 6),
        ('LEFTPADDING', (0, 0), (-1, -1), 8),
        ('RIGHTPADDING', (0, 0), (-1, -1), 8),
    ]))
    story.append(inc_table)
    story.append(Spacer(1, 14))

    # Signatures
    sig_data = [
        [
            Paragraph("<b>PREPARED & OFFERED BY:</b>", ParagraphStyle('S1', fontName='Helvetica-Bold', fontSize=7.5, textColor=colors.HexColor('#64748b'))),
            Paragraph("<b>ACCEPTED & CONFORME:</b>", ParagraphStyle('S2', fontName='Helvetica-Bold', fontSize=7.5, textColor=colors.HexColor('#64748b')))
        ],
        [
            Spacer(1, 24),
            Spacer(1, 24)
        ],
        [
            Paragraph("<b>DIONI / LEAD DEVELOPER</b><br/><font size=7 color='#64748b'>DIONIX Software Solutions</font>", ParagraphStyle('S3', fontName='Helvetica', fontSize=8, leading=10, alignment=0)),
            Paragraph("<b>CLIENT AUTHORIZED REPRESENTATIVE</b><br/><font size=7 color='#64748b'>Signature Over Printed Name / Date</font>", ParagraphStyle('S4', fontName='Helvetica', fontSize=8, leading=10, alignment=0))
        ]
    ]
    sig_table = Table(sig_data, colWidths=[260, 263])
    sig_table.setStyle(TableStyle([
        ('LINEBELOW', (0, 1), (0, 1), 0.8, colors.HexColor('#0f172a')),
        ('LINEBELOW', (1, 1), (1, 1), 0.8, colors.HexColor('#0f172a')),
        ('LEFTPADDING', (0, 0), (-1, -1), 0),
        ('RIGHTPADDING', (0, 0), (-1, -1), 0),
        ('TOPPADDING', (0, 0), (-1, -1), 2),
        ('BOTTOMPADDING', (0, 0), (-1, -1), 2),
    ]))
    story.append(sig_table)
    story.append(Spacer(1, 8))

    story.append(Paragraph(
        "<font size=6.5 color='#94a3b8'>"
        "Payment Terms: 50% downpayment upon system deployment and 50% upon completed training & sign-off. "
        "Standard commercial desktop software pricing benchmarked against Philippine software industry averages."
        "</font>",
        ParagraphStyle('Foot', fontName='Helvetica', fontSize=6.5, leading=8.5, alignment=1)
    ))

    doc.build(story)
    print(f"PDF successfully generated at: {output_path}")

if __name__ == '__main__':
    pdf_path = os.path.abspath("DIONIX_POS_Official_Proposal.pdf")
    generate_pdf(pdf_path)
