(() => {
    const root = document.getElementById("runsDashboard");
    if (!root || typeof DevExpress === "undefined" || !DevExpress.data?.AspNet) {
        return;
    }

    const selectedRunLabel = document.getElementById("selectedRunLabel");
    const selectedSnapshotLabel = document.getElementById("selectedSnapshotLabel");
    const productContextLabel = document.getElementById("productContextLabel");

    const snapshotEmptyNoRun = "Select a run to view snapshots";
    const snapshotEmptyNoData = "No snapshots found for selected run";
    const productEmptyNoSnapshot = "Select a snapshot to view products";
    const productEmptyNoData = "No products found for selected snapshot";

    let selectedRunId = null;
    let selectedSnapshotId = null;

    const createStore = (url, extraDataFactory) => DevExpress.data.AspNet.createStore({
        key: "id",
        loadUrl: url,
        onBeforeSend(method, ajaxOptions) {
            if (extraDataFactory) {
                ajaxOptions.data = {
                    ...ajaxOptions.data,
                    ...extraDataFactory()
                };
            }
        }
    });

    const refreshContextLabels = () => {
        selectedRunLabel.textContent = selectedRunId === null ? "Selected Run: none" : `Selected Run: #${selectedRunId}`;
        selectedSnapshotLabel.textContent = selectedSnapshotId === null
            ? "Selected Snapshot: none"
            : `Selected Snapshot: #${selectedSnapshotId}`;
        productContextLabel.textContent = selectedSnapshotId === null
            ? productEmptyNoSnapshot
            : `Selected Snapshot: #${selectedSnapshotId}`;
    };

    $("#topToolbar").dxToolbar({
        elementAttr: {class: "top-toolbar"},
        items: [
            {
                location: "before",
                template() {
                    return $("<div>").addClass("brand-block")
                        .append($("<div>").addClass("brand-icon").attr("aria-hidden", "true").text("VP"))
                        .append($("<div>").addClass("brand-title").text("VarPrice Pro"));
                }
            },
            {
                location: "center",
                locateInMenu: "auto",
                template() {
                    return $("<div>").addClass("top-nav")
                        .append($("<span>").addClass("top-nav-item active").text("Product"))
                        .append($("<span>").addClass("top-nav-item").text("Solutions"))
                        .append($("<span>").addClass("top-nav-item").text("Pricing"))
                        .append($("<span>").addClass("top-nav-item").text("Resources"))
                        .append($("<span>").addClass("top-nav-item").text("Company"));
                }
            },
            {
                location: "after",
                widget: "dxButton",
                options: {
                    text: "Sign Up",
                    stylingMode: "outlined",
                    elementAttr: {class: "top-btn top-btn-light"}
                }
            },
            {
                location: "after",
                widget: "dxButton",
                options: {
                    text: "Start Free Trial",
                    stylingMode: "contained",
                    elementAttr: {class: "top-btn top-btn-dark"}
                }
            }
        ]
    });

    $("#workspaceTabs").dxTabs({
        dataSource: [
            {text: "Dashboard"},
            {text: "Properties"},
            {text: "Analytics"},
            {text: "Finance"},
            {text: "Documents"}
        ],
        selectedIndex: 0,
        focusStateEnabled: false,
        hoverStateEnabled: true,
        itemTemplate(itemData) {
            return $("<span>").addClass("tab-pill-text").text(itemData.text);
        },
        elementAttr: {class: "workspace-tabs"}
    });

    $("#ingestVegetablesButton").dxButton({
        text: "Collect price snapshot (vegetables)",
        type: "default",
        stylingMode: "contained",
        elementAttr: {class: "dashboard-primary-button"},
        onClick() {
            document.getElementById("ingestForm")?.submit();
        }
    });

    const runsGrid = $("#runsGrid").dxDataGrid({
        dataSource: createStore(root.dataset.runsUrl),
        keyExpr: "id",
        remoteOperations: true,
        showBorders: false,
        hoverStateEnabled: true,
        noDataText: "No crawler runs found",
        paging: {pageSize: 25},
        pager: {
            showPageSizeSelector: true,
            allowedPageSizes: [25, 50, 100],
            showInfo: true,
            visible: true
        },
        searchPanel: {visible: true, width: 240, placeholder: "Search runs"},
        headerFilter: {visible: true},
        selection: {mode: "single"},
        sorting: {mode: "multiple"},
        columns: [
            {dataField: "id", caption: "Id", width: 90},
            {dataField: "startedAtUtc", caption: "StartedAtUtc", dataType: "datetime", sortOrder: "desc"},
            {dataField: "status", caption: "Status", minWidth: 120},
            {dataField: "finishedAtUtc", caption: "FinishedAtUtc", dataType: "datetime"},
            {dataField: "itemsCount", caption: "ItemsCount", width: 110}
        ],
        onSelectionChanged(e) {
            const row = e.selectedRowsData[0];
            selectedRunId = row ? row.id : null;
            selectedSnapshotId = null;
            refreshContextLabels();

            snapshotsGrid.option("noDataText", selectedRunId === null ? snapshotEmptyNoRun : snapshotEmptyNoData);
            productsGrid.option("noDataText", productEmptyNoSnapshot);
            snapshotsGrid.refresh();
            productsGrid.refresh();
        }
    }).dxDataGrid("instance");

    const snapshotsGrid = $("#snapshotsGrid").dxDataGrid({
        dataSource: createStore(root.dataset.snapshotsUrl, () => ({runId: selectedRunId})),
        keyExpr: "id",
        remoteOperations: true,
        showBorders: false,
        hoverStateEnabled: true,
        noDataText: snapshotEmptyNoRun,
        paging: {pageSize: 25},
        pager: {
            showPageSizeSelector: true,
            allowedPageSizes: [25, 50, 100],
            showInfo: true,
            visible: true
        },
        searchPanel: {visible: true, width: 240, placeholder: "Search snapshots"},
        headerFilter: {visible: true},
        selection: {mode: "single"},
        sorting: {mode: "multiple"},
        columns: [
            {dataField: "id", caption: "Id", width: 90},
            {dataField: "createdAtUtc", caption: "CreatedAtUtc", dataType: "datetime", sortOrder: "desc"},
            {dataField: "city", caption: "City", minWidth: 120},
            {dataField: "price", caption: "Price", dataType: "number", format: {type: "fixedPoint", precision: 2}},
            {
                dataField: "oldPrice",
                caption: "OldPrice",
                dataType: "number",
                format: {type: "fixedPoint", precision: 2}
            },
            {
                dataField: "discountPercent",
                caption: "Discount",
                dataType: "number",
                format: {type: "fixedPoint", precision: 1}
            },
            {dataField: "promoFlag", caption: "Promo", dataType: "boolean", width: 90},
            {dataField: "inStock", caption: "InStock", dataType: "boolean", width: 100}
        ],
        onRowPrepared(e) {
            if (e.rowType !== "data") {
                return;
            }

            const isPromo = e.data?.promoFlag === true;
            const isStrongDiscount = isPromo && e.data?.discountPercent !== null && e.data?.discountPercent > 25;

            $(e.rowElement).toggleClass("snapshot-promo", isPromo);
            $(e.rowElement).toggleClass("snapshot-promo-strong", isStrongDiscount);
        },
        onSelectionChanged(e) {
            const row = e.selectedRowsData[0];
            selectedSnapshotId = row ? row.id : null;
            refreshContextLabels();

            productsGrid.option("noDataText", selectedSnapshotId === null ? productEmptyNoSnapshot : productEmptyNoData);
            productsGrid.refresh();
        }
    }).dxDataGrid("instance");

    const productsGrid = $("#productsGrid").dxDataGrid({
        dataSource: createStore(root.dataset.productsUrl, () => ({snapshotId: selectedSnapshotId})),
        keyExpr: "id",
        remoteOperations: true,
        showBorders: false,
        hoverStateEnabled: true,
        noDataText: productEmptyNoSnapshot,
        paging: {pageSize: 50},
        pager: {
            showPageSizeSelector: true,
            allowedPageSizes: [25, 50, 100],
            showInfo: true,
            visible: true
        },
        searchPanel: {visible: true, width: 240, placeholder: "Search products"},
        headerFilter: {visible: true},
        sorting: {mode: "multiple"},
        columns: [
            {dataField: "id", caption: "Id", width: 90},
            {dataField: "name", caption: "Name", minWidth: 220},
            {dataField: "sku", caption: "Sku", minWidth: 130},
            {
                dataField: "url",
                caption: "URL",
                minWidth: 220,
                cellTemplate(container, options) {
                    if (!options.value) {
                        return;
                    }

                    $("<a>")
                        .attr("href", options.value)
                        .attr("target", "_blank")
                        .attr("rel", "noopener noreferrer")
                        .text(options.value)
                        .appendTo(container);
                }
            },
            {
                dataField: "price",
                caption: "Price",
                dataType: "number",
                format: {type: "fixedPoint", precision: 2},
                width: 110
            },
            {dataField: "unit", caption: "Unit", minWidth: 100},
            {dataField: "updatedAtUtc", caption: "UpdatedAtUtc", dataType: "datetime", sortOrder: "asc"}
        ]
    }).dxDataGrid("instance");

    refreshContextLabels();
})();
