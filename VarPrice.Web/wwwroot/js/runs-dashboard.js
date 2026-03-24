(() => {
    const root = document.getElementById("runsDashboard");
    if (!root || typeof window.kendo === "undefined" || typeof window.jQuery === "undefined") {
        return;
    }

    const $ = window.jQuery;
    const selectedRunLabel = document.getElementById("selectedRunLabel");
    const selectedSnapshotLabel = document.getElementById("selectedSnapshotLabel");
    const productContextLabel = document.getElementById("productContextLabel");

    const snapshotScopes = {
        none: "none",
        all: "all",
        successful: "successful",
        failed: "failed"
    };

    const snapshotEmptyNoSelection = "Select a run or snapshot group to view snapshots";
    const snapshotEmptyByScope = {
        [snapshotScopes.all]: "No snapshots found for selected run",
        [snapshotScopes.successful]: "No successful snapshots found for selected run",
        [snapshotScopes.failed]: "No failed snapshots found for selected run"
    };
    const productEmptyNoSnapshot = "Select a snapshot to view products";
    const productEmptyNoData = "No products found for selected snapshot";

    let selectedRunId = null;
    let selectedSnapshotId = null;
    let selectedSnapshotScope = snapshotScopes.none;
    let selectedTreeNode = null;
    let pendingTreeSelectionId = null;
    let shouldExpandRoots = true;

    const encode = (value) => window.kendo.htmlEncode(value ?? "");
    const formatDateTime = (value) => value ? window.kendo.toString(value, "yyyy-MM-dd HH:mm") : "";
    const readResultValue = (response, camelCaseKey, pascalCaseKey, fallbackValue) => {
        if (!response || typeof response !== "object") {
            return fallbackValue;
        }

        if (response[camelCaseKey] !== undefined) {
            return response[camelCaseKey];
        }

        if (response[pascalCaseKey] !== undefined) {
            return response[pascalCaseKey];
        }

        return fallbackValue;
    };

    const createGridDataSource = (url, extraDataFactory, fields, pageSize, sort) => new window.kendo.data.DataSource({
        transport: {
            read: {
                url,
                dataType: "json",
                data: extraDataFactory ?? (() => ({}))
            }
        },
        schema: {
            data: (response) => readResultValue(response, "data", "Data", []),
            total: (response) => readResultValue(response, "total", "Total", 0),
            errors: (response) => readResultValue(response, "errors", "Errors", null),
            model: {
                id: "id",
                fields
            }
        },
        pageSize,
        sort,
        serverPaging: true,
        serverSorting: true,
        serverFiltering: true
    });

    const createTreeDataSource = () => new window.kendo.data.TreeListDataSource({
        transport: {
            read: {
                url: root.dataset.runsTreeUrl,
                dataType: "json"
            }
        },
        schema: {
            model: {
                id: "id",
                parentId: "parentId",
                fields: {
                    id: {type: "string"},
                    parentId: {nullable: true},
                    nodeType: {type: "string"},
                    title: {type: "string"},
                    runId: {type: "number", nullable: true},
                    snapshotScope: {type: "string"},
                    startedAtUtc: {type: "date", nullable: true},
                    finishedAtUtc: {type: "date", nullable: true},
                    status: {type: "string"},
                    itemsCount: {type: "number", nullable: true}
                },
                expanded: false
            }
        }
    });

    const reloadGrid = (grid) => {
        if (grid.dataSource.page() !== 1) {
            grid.dataSource.page(1);
            return;
        }

        grid.dataSource.read();
    };

    const updateNoRecordsText = (grid, text) => {
        grid.wrapper.find(".k-grid-norecords td").text(text);
    };

    const getSnapshotEmptyText = () => {
        if (selectedRunId === null) {
            return snapshotEmptyNoSelection;
        }

        return snapshotEmptyByScope[selectedSnapshotScope] ?? snapshotEmptyByScope[snapshotScopes.all];
    };

    const describeTreeSelection = (node) => {
        if (!node) {
            return "Selected node: none";
        }

        switch (node.nodeType) {
            case "date":
                return `Selected date: ${node.title}`;
            case "run":
                return `Selected run: #${node.runId}`;
            case "successful":
                return `Selected group: Successful snapshots for run #${node.runId}`;
            case "failed":
                return `Selected group: Failed snapshots for run #${node.runId}`;
            default:
                return `Selected node: ${node.title}`;
        }
    };

    const refreshContextLabels = () => {
        selectedRunLabel.textContent = describeTreeSelection(selectedTreeNode);
        selectedSnapshotLabel.textContent = selectedSnapshotId === null
            ? "Selected Snapshot: none"
            : `Selected Snapshot: #${selectedSnapshotId}`;
        productContextLabel.textContent = selectedSnapshotId === null
            ? productEmptyNoSnapshot
            : `Selected Snapshot: #${selectedSnapshotId}`;
    };

    const treeIcon = (nodeType) => {
        switch (nodeType) {
            case "date":
                return `
                    <svg viewBox="0 0 24 24" aria-hidden="true">
                        <path d="M16 2v4"></path>
                        <path d="M8 2v4"></path>
                        <path d="M3 10h18"></path>
                        <rect x="3" y="4" width="18" height="17" rx="3"></rect>
                    </svg>`;
            case "run":
                return `
                    <svg viewBox="0 0 24 24" aria-hidden="true">
                        <path d="M6 4h12a2 2 0 0 1 2 2v12a2 2 0 0 1 -2 2H6a2 2 0 0 1 -2 -2V6a2 2 0 0 1 2 -2"></path>
                        <path d="M10 8l6 4l-6 4z"></path>
                    </svg>`;
            case "successful":
                return `
                    <svg viewBox="0 0 24 24" aria-hidden="true">
                        <circle cx="12" cy="12" r="9"></circle>
                        <path d="M8.5 12.5l2.5 2.5l4.5 -5"></path>
                    </svg>`;
            case "failed":
                return `
                    <svg viewBox="0 0 24 24" aria-hidden="true">
                        <circle cx="12" cy="12" r="9"></circle>
                        <path d="M9 9l6 6"></path>
                        <path d="M15 9l-6 6"></path>
                    </svg>`;
            default:
                return `
                    <svg viewBox="0 0 24 24" aria-hidden="true">
                        <circle cx="12" cy="12" r="9"></circle>
                    </svg>`;
        }
    };

    const renderRunStatus = (status) => {
        if (!status) {
            return "";
        }

        return `<span class="tree-status tree-status-${encode(status)}">${encode(status)}</span>`;
    };

    const renderTreeTitle = (dataItem) => `
        <span class="tree-node tree-node-${encode(dataItem.nodeType)}">
            <span class="tree-node-icon">${treeIcon(dataItem.nodeType)}</span>
            <span class="tree-node-text">${encode(dataItem.title)}</span>
        </span>`;

    const hasTreeNodeId = (value) => value !== null && value !== undefined && value !== "";

    const getTreeRowById = (treeList, nodeId) => treeList.tbody.children("tr").filter((_, row) => {
        const dataItem = treeList.dataItem(row);
        return dataItem?.id === nodeId;
    }).first();

    const clearTreeSelection = (treeList) => {
        if (typeof treeList.clearSelection === "function") {
            treeList.clearSelection();
            return;
        }

        treeList.select($());
    };

    const expandTreeAncestors = (treeList, dataItem) => {
        if (!dataItem) {
            return;
        }

        const ancestorIds = [];
        let parentId = dataItem.parentId;

        while (hasTreeNodeId(parentId)) {
            ancestorIds.unshift(parentId);
            parentId = treeList.dataSource.get(parentId)?.parentId ?? null;
        }

        ancestorIds.forEach((ancestorId) => {
            const ancestorRow = getTreeRowById(treeList, ancestorId);
            if (ancestorRow.length > 0) {
                treeList.expand(ancestorRow);
            }
        });
    };

    const applyTreeSelection = (node, options = {}) => {
        const {reloadRelated = true, resetSnapshotSelection = true} = options;

        selectedTreeNode = node ?? null;
        selectedRunId = node?.runId ?? null;
        selectedSnapshotScope = node?.snapshotScope ?? snapshotScopes.none;

        if (resetSnapshotSelection) {
            selectedSnapshotId = null;
        }

        refreshContextLabels();

        if (!reloadRelated) {
            return;
        }

        snapshotsGrid.clearSelection();
        productsGrid.clearSelection();
        reloadGrid(snapshotsGrid);
        reloadGrid(productsGrid);
    };

    const restoreTreeSelection = (treeList) => {
        if (pendingTreeSelectionId === null) {
            return;
        }

        const nodeIdToRestore = pendingTreeSelectionId;
        pendingTreeSelectionId = null;

        const restoredItem = treeList.dataSource.get(nodeIdToRestore);
        if (!restoredItem) {
            clearTreeSelection(treeList);
            applyTreeSelection(null);
            return;
        }

        expandTreeAncestors(treeList, restoredItem);

        const restoredRow = getTreeRowById(treeList, nodeIdToRestore);
        if (restoredRow.length === 0) {
            clearTreeSelection(treeList);
            applyTreeSelection(null);
            return;
        }

        treeList.select(restoredRow);
        applyTreeSelection(treeList.dataItem(restoredRow), {
            reloadRelated: false,
            resetSnapshotSelection: false
        });
    };

    const refreshTreeList = () => {
        const treeList = $("#runsTreeList").data("kendoTreeList");
        if (!treeList) {
            return;
        }

        pendingTreeSelectionId = selectedTreeNode?.id ?? null;
        shouldExpandRoots = true;
        treeList.dataSource.read();
    };

    $("#topToolbar").kendoToolBar({
        items: [
            {
                template: "<div class='brand-block'><div class='brand-icon' aria-hidden='true'>VP</div><div class='brand-title'>VarPrice Pro</div></div>",
                overflow: "never"
            },
            {type: "spacer"},
            {
                template: "<div class='top-nav'><span class='top-nav-item active'>Product</span><span class='top-nav-item'>Solutions</span><span class='top-nav-item'>Pricing</span><span class='top-nav-item'>Resources</span><span class='top-nav-item'>Company</span></div>",
                overflow: "never"
            },
            {type: "spacer"},
            {
                type: "button",
                text: "Sign Up",
                attributes: {class: "top-btn top-btn-light"},
                overflow: "never"
            },
            {
                type: "button",
                text: "Start Free Trial",
                attributes: {class: "top-btn top-btn-dark"},
                overflow: "never"
            }
        ]
    });

    $("#workspaceTabs").kendoButtonGroup({
        index: 0,
        selection: "single",
        items: [
            {text: "Dashboard"},
            {text: "Properties"},
            {text: "Analytics"},
            {text: "Finance"},
            {text: "Documents"}
        ]
    });

    $("#ingestVegetablesButton").kendoButton({
        themeColor: "success",
        fillMode: "solid",
        rounded: "full",
        size: "large",
        icon: "arrow-rotate-cw",
        click() {
            document.getElementById("ingestForm")?.submit();
        }
    });
    $("#ingestVegetablesButton").addClass("dashboard-primary-button");
    $("#refreshRunsTreeButton").on("click", refreshTreeList);

    $("#runsTreeList").kendoTreeList({
        dataSource: createTreeDataSource(),
        selectable: "row",
        sortable: false,
        resizable: true,
        scrollable: true,
        columns: [
            {
                field: "title",
                title: "Run / Group",
                expandable: true,
                minResizableWidth: 240,
                width: 360,
                template: renderTreeTitle
            },
            {
                field: "status",
                title: "Status",
                width: 120,
                template: (dataItem) => dataItem.nodeType === "run" ? renderRunStatus(dataItem.status) : ""
            },
            {
                field: "startedAtUtc",
                title: "StartedAtUtc",
                width: 165,
                template: (dataItem) => dataItem.nodeType === "run" ? encode(formatDateTime(dataItem.startedAtUtc)) : ""
            },
            {
                field: "finishedAtUtc",
                title: "FinishedAtUtc",
                width: 165,
                template: (dataItem) => dataItem.nodeType === "run" ? encode(formatDateTime(dataItem.finishedAtUtc)) : ""
            },
            {
                field: "itemsCount",
                title: "ItemsCount",
                width: 110,
                attributes: {class: "tree-metric-cell"},
                template: (dataItem) => dataItem.itemsCount ?? ""
            }
        ],
        dataBound(e) {
            if (shouldExpandRoots) {
                e.sender.tbody.children("tr").each((_, row) => {
                    const dataItem = e.sender.dataItem(row);
                    if (dataItem?.parentId == null) {
                        e.sender.expand(row);
                    }
                });
                shouldExpandRoots = false;
            }

            restoreTreeSelection(e.sender);
        },
        change() {
            const row = this.select();
            const item = row.length > 0 ? this.dataItem(row) : null;
            applyTreeSelection(item);
        }
    });

    const snapshotsGrid = $("#snapshotsGrid").kendoGrid({
        dataSource: createGridDataSource(
            root.dataset.snapshotsUrl,
            () => ({
                runId: selectedRunId,
                snapshotScope: selectedSnapshotScope
            }),
            {
                id: {type: "number"},
                createdAtUtc: {type: "date"},
                city: {type: "string"},
                status: {type: "string"},
                price: {type: "number"},
                oldPrice: {type: "number"},
                discountPercent: {type: "number"},
                promoFlag: {type: "boolean"},
                inStock: {type: "boolean"}
            },
            25,
            [{field: "createdAtUtc", dir: "desc"}]
        ),
        selectable: "row",
        sortable: {
            mode: "multiple",
            allowUnsort: true,
            showIndexes: true
        },
        filterable: true,
        columnMenu: true,
        resizable: true,
        scrollable: true,
        noRecords: {
            template: snapshotEmptyNoSelection
        },
        pageable: {
            refresh: true,
            pageSizes: [25, 50, 100],
            buttonCount: 5
        },
        toolbar: ["search"],
        search: {
            fields: ["id", "city", "status"]
        },
        columns: [
            {field: "id", title: "Id", width: 90},
            {field: "status", title: "Status", width: 120},
            {field: "createdAtUtc", title: "CreatedAtUtc", format: "{0:yyyy-MM-dd HH:mm}"},
            {field: "city", title: "City", minResizableWidth: 140},
            {field: "price", title: "Price", format: "{0:n2}"},
            {field: "oldPrice", title: "OldPrice", format: "{0:n2}"},
            {field: "discountPercent", title: "Discount", format: "{0:n1}"},
            {
                field: "promoFlag",
                title: "Promo",
                width: 90,
                template: (dataItem) => dataItem.promoFlag ? "Yes" : "No"
            },
            {
                field: "inStock",
                title: "InStock",
                width: 100,
                template: (dataItem) => dataItem.inStock ? "Yes" : "No"
            }
        ],
        dataBound(e) {
            updateNoRecordsText(this, getSnapshotEmptyText());

            e.sender.tbody.children("tr").each((_, row) => {
                const dataItem = e.sender.dataItem(row);
                if (!dataItem) {
                    return;
                }

                const isPromo = dataItem.promoFlag === true;
                const isStrongDiscount = isPromo
                    && dataItem.discountPercent !== null
                    && dataItem.discountPercent > 25;

                $(row).toggleClass("snapshot-promo", isPromo);
                $(row).toggleClass("snapshot-promo-strong", isStrongDiscount);
            });
        },
        change() {
            const row = this.dataItem(this.select());
            selectedSnapshotId = row ? row.id : null;
            refreshContextLabels();
            reloadGrid(productsGrid);
        }
    }).data("kendoGrid");

    const productsGrid = $("#productsGrid").kendoGrid({
        dataSource: createGridDataSource(
            root.dataset.productsUrl,
            () => ({snapshotId: selectedSnapshotId}),
            {
                id: {type: "number"},
                name: {type: "string"},
                sku: {type: "string"},
                url: {type: "string"},
                price: {type: "number"},
                unit: {type: "string"},
                updatedAtUtc: {type: "date"}
            },
            50,
            [{field: "updatedAtUtc", dir: "asc"}]
        ),
        sortable: {
            mode: "multiple",
            allowUnsort: true,
            showIndexes: true
        },
        filterable: true,
        columnMenu: true,
        resizable: true,
        scrollable: true,
        noRecords: {
            template: productEmptyNoSnapshot
        },
        pageable: {
            refresh: true,
            pageSizes: [25, 50, 100],
            buttonCount: 5
        },
        toolbar: ["search"],
        search: {
            fields: ["id", "name", "sku", "url", "unit"]
        },
        columns: [
            {field: "id", title: "Id", width: 90},
            {field: "name", title: "Name", minResizableWidth: 220},
            {field: "sku", title: "Sku", minResizableWidth: 130},
            {
                field: "url",
                title: "URL",
                minResizableWidth: 260,
                template(dataItem) {
                    if (!dataItem.url) {
                        return "";
                    }

                    const safeUrl = encode(dataItem.url);
                    return `<a href="${safeUrl}" target="_blank" rel="noopener noreferrer">${safeUrl}</a>`;
                }
            },
            {field: "price", title: "Price", format: "{0:n2}", width: 110},
            {field: "unit", title: "Unit", minResizableWidth: 100},
            {field: "updatedAtUtc", title: "UpdatedAtUtc", format: "{0:yyyy-MM-dd HH:mm}"}
        ],
        dataBound() {
            updateNoRecordsText(this, selectedSnapshotId === null ? productEmptyNoSnapshot : productEmptyNoData);
        }
    }).data("kendoGrid");

    refreshContextLabels();
})();
